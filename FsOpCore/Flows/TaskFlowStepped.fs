namespace FsOpCore
open System
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open FlUtils

module TaskFlowStepped =
    ///controls how many CUA turns to do before getting reasoner guidance
    let MAXC = 5

    ///flow input messages
    type TaskFlowMsgIn =
        | TFi_Start
        | TFi_Resume of string
        | TFi_EndAndReport
        | TFi_Step

    ///flow output messages
    type TaskFlowMsgOut =
        | TFo_Paused of ChatMsg list
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Usage of Map<string,FsResponses.Usage list>
        | TFo_Done of TaskState<TaskFlowMsgIn,TaskFlowMsgOut>

    ///keeps needed local 'substate'
    type SubState = {
        task                : TaskState<TaskFlowMsgIn,TaskFlowMsgOut>
        cts                 : CancellationTokenSource
        visualState         : VisualState option
        corrId              : string
        cuaLoopCount        : int
        error               : WErrorType option
        cuaResp             : FsResponses.Response option
    }
        with
            static member Create task =
                {
                    task = task
                    cts = new CancellationTokenSource()
                    corrId = ""
                    cuaLoopCount = 0
                    visualState = None
                    error = None
                    cuaResp = None

                }
            member this.setTask v : SubState = {this with task = v} // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this v
            member this.setTask2 (v,x)  = {this with task = v},x // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this
            member this.lastActionM() = this.task.lastAction() |> List.map TFo_Action
            member this.appendUsage = function W_Cua resp | W_Reasoner (_, resp) -> {this with task = FlUtils.getUsage resp |> this.task.appendUsage} | _ -> this
            member this.setCorrId id = {this with corrId = id}
            member this.incrCuaLoopCount()  = {this with cuaLoopCount = this.cuaLoopCount + 1}
            member this.resetCuaLoopCount() = {this with cuaLoopCount = 0}
            member this.setVisualState vs = {this with visualState = vs}
            member this.setError e = {this with error = Some e}
            member this.setCuaResponse r = {this with cuaResp = Some r}

            member this.performComputerCall resp =
                async{
                    let! t,vstate = Cua.performComputerCall this.task resp
                    return {this with task = t; visualState=vstate}
                }

            member this.callFunctions resp =
                async {
                    let! task = Reasoner.callFunctions this.task resp
                    return {this with task = task}
                }

    //state machine
    module States =
        let snapshot (ss:SubState) = async {
            let! vs = FlUtils.snapshot ss.task.driver
            let ss = ss.setVisualState (Some vs)
            let ss = ss.setTask (ss.task.prependSnapshot vs.snapshot)
            return ss
        }

        let getOrGenerateSteps (ss:SubState) =
            if ss.task.steps.steps.IsEmpty then 
                let id = Reasoner.breakTaskIntoSteps ss.task
                let ss = ss.setCorrId id
                Choice1Of2 ss  //no steps yet, getting them
            else
                Choice2Of2 ss //have steps

        ///matches if we guidance from reasoner before responding to CUA
        let (|GetReasonerGuidance|_|) (ss:SubState) msg =
            match msg with
            | W_Cua resp when ss.cuaLoopCount >= MAXC && ss.task.reasonerPrompt.IsSome -> Some resp
            | _                                                                        -> None

        ///with the given correlation id
        let (|Reasoner|_|) corrId msg =
            match msg with
            | W_Reasoner (id,resp) when id = corrId -> Some resp
            | _                                     -> None

        ///matches if reasoner response contains steps
        let (|GotSteps|_|) (ss:SubState) msg =
            match msg with
            | W_Reasoner (id,resp) when id = ss.corrId ->
                    let text = RUtils.outputText resp
                    try 
                        let s = Text.Json.JsonSerializer.Deserialize<CuaInstructions>(text, FlUtils.openAIResponseSerOpts) 
                        s.steps |> List.iter (fun x -> Log.info $"{x.step_num}: {x.step_instructions}")
                        Some s.steps 
                    with ex -> 
                        Log.exn(ex,nameof GotSteps)
                        None
            | _ ->  None

        ///resume with CUA loop after receiving guidance
        let (|Resume|_|) msg =
            match msg with
            | W_App (TFi_Resume tx) -> Some tx
            | _                     -> None

        ///capture common state processing here
        let rec (|Txn|TxnAsync|Cont|)  (s_ret,ss:SubState,msg) =
            let ss = ss.appendUsage msg
            match msg with
            | W_Err e                        -> let ss = ss.setError e
                                                Txn(F(s_terminate ss, [TFo_Usage ss.task.usage]))
            | W_App TFi_EndAndReport         -> let corrId = Reasoner.stopAndSummarizeStep ss.task
                                                let ss = ss.setCorrId corrId
                                                Txn(F(s_summarizing ss,[TFo_Usage ss.task.usage]))
            | Cua_FuncCall (resp)            -> TxnAsync (
                                                    async {
                                                        let! fouts = Cua.callFunctions ss.task resp
                                                        let ss = ss.setCuaResponse resp
                                                        Cua.postCuaFuncResults ss.task resp fouts
                                                        return F(s_ret ss,[TFo_Usage ss.task.usage])
                                                    })
            | FuncCall ss.corrId (resp)      -> TxnAsync (
                                                    async {
                                                        let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                        let! ss = ss.callFunctions resp
                                                        Reasoner.postFunctionCall ss.corrId ss.task
                                                        return F(s_ret ss, [TFo_Usage ss.task.usage])
                                                    })
            | _                              -> Cont (ss,[TFo_Usage ss.task.usage],msg)


        (* --- states --- *)

        and s_start ss msg = async {
            Log.info $"in {nameof s_start} task '{ss.task.id}'"
            match s_start,ss,msg with
            | Txn st                         -> return st
            | TxnAsync st                    -> return! st
            | Cont (ss,ms, W_App TFi_Start)  -> let ss' = getOrGenerateSteps ss
                                                do! ss.task.driver.start ss.task.target
                                                match ss' with
                                                | Choice1Of2 ss -> return F(s_start ss,ms)  //no steps yet, need to wait for 'GotSteps'
                                                | Choice2Of2 ss -> ss.task.bus.PostInput (W_App TFi_Step)
                                                                   return F(s_step ss,ms)   //start processing steps
            | Cont (ss,ms,GotSteps ss xs)    -> let ss = ss.setTask (ss.task.setSteps (xs |> List.map CuaStep.Create))
                                                ss.task.bus.PostInput (W_App TFi_Step)
                                                return F(s_step ss,ms)
            | Cont(ss,ms,x)                  -> Log.warn $"{nameof s_start}: expecting TFi_Start or W_Reasoner with steps message to start flow but got {x}"
                                                return F(s_start ss,ms)
        }

        and s_step ss msg = async {
            Log.info $"in {nameof s_step} task '{ss.task.id}'"
            match s_step,ss,msg with
            | Txn st                         -> return st
            | TxnAsync st                    -> return! st
            | Cont (ss,ms,W_App TFi_Step)    -> match ss.task.steps.NextToDo() with
                                                | Some step ->
                                                    Log.info $"step {step.step.step_num}: {step.step.step_instructions |> shorten 120}"
                                                    let ss = {ss with task.steps = ss.task.steps.SetCurrentStep step.step.step_num}
                                                    let ss = ss.setTask (ss.task.clearReasonerHistory())
                                                    let! ss = snapshot ss
                                                    let req =
                                                        {CuaReq.Default with 
                                                          instructions=(Some step.step.step_instructions)
                                                          visualState=ss.visualState.Value
                                                          nonCuaTools=ss.task.toolDefs |> List.map Tool.Function
                                                        }
                                                    FlResps.postCuaRequest ss.task.bus.PostInput req
                                                    return F(s_cua ss,ms)
                                                | None ->
                                                    return F(s_terminate ss,TFo_Done ss.task::ms)
            | Cont (ss,_,x)                  -> return ignoreMsg (s_step ss) x (nameof s_step)
        }

        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} task '{ss.task.id}'"
            match s_cua,ss,msg with
            | Txn st                                    -> return st
            | TxnAsync st                               -> return! st

            | Cont (ss,ms, NoComputerCall resp)         -> let ss,_ = ss.setTask2 (Cua.stepPrependAsstMsg ss.task resp)
                                                           let ss = {ss with task.steps = ss.task.steps.AdvanceStep()}
                                                           let ss = ss.resetCuaLoopCount()
                                                           ss.task.bus.PostInput (W_App TFi_Step) //start next step, if any
                                                           return F(s_step ss,ms)
            | Cont (ss,ms, GetReasonerGuidance ss resp) -> //should get guidance from reasoner before responding to CUA
                                                           let ss = {ss with task = ss.task.clearReasonerHistory()}
                                                           let ss,_ = ss.setTask2 (Cua.stepPrependAsstMsg ss.task resp)
                                                           let! ss = ss.performComputerCall resp
                                                           let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                           let corrId = Reasoner.getGuidanceForCuaStep ss.task ss.task.reasonerPrompt.Value
                                                           let ss = ss.setCorrId corrId
                                                           let ss = ss.setCuaResponse resp
                                                           return F(s_reason ss,outMsgs @ ms)
            | Cont (ss,ms, W_Cua resp)                  -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp) //cont. w/out rsnr guidance
                                                           let! ss = ss.performComputerCall resp
                                                           let ss = ss.setCuaResponse resp
                                                           Cua.postCuaNext ss.task ss.visualState resp None
                                                           let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                           return F(s_cua ss,outMsgs @ ms) //increment count
            | Cont (ss,_,x)                             -> return ignoreMsg (s_cua ss) x (nameof s_cua)
        }

        and s_reason ss msg  = async {
            let ss = ss.resetCuaLoopCount()
            Log.info $"in {nameof s_reason} task '{ss.task.id}'"
            match s_reason,ss,msg with
            | Txn st                               -> return st
            | TxnAsync st                          -> return! st
            | Cont (ss,ms,Reasoner ss.corrId resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                      match Reasoner.reasonerGuidance resp with
                                                      | None ->
                                                            Log.info $"step {ss.task.steps.stepIndex} completed"
                                                            let ss = {ss with task.steps = ss.task.steps.AdvanceStep()}
                                                            ss.task.bus.PostInput (W_App TFi_Step) //start next step, if any
                                                            return F(s_step ss,ms)
                                                      | Some guidance ->
                                                            Cua.postCuaNext ss.task ss.visualState ss.cuaResp.Value (Some guidance)
                                                            return F(s_cua ss,ms)
            | Cont(ss,_,x)                         -> return ignoreMsg (s_reason ss) x (nameof s_reason)
        }

        and s_summarizing ss msg = async {
            Log.info $"in {nameof s_summarizing} task '{ss.task.id}'"
            match s_summarizing,ss,msg with
            | Txn st                                -> return st
            | TxnAsync st                           -> return! st
            | Cont (ss,ms, Reasoner ss.corrId resp) -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                       return F(s_terminate ss, TFo_Done ss.task::ms)
            | Cont (ss,_,x)                         -> return ignoreMsg (s_summarizing ss) x (nameof s_summarizing)
        }

        and s_terminate ss msg = async {
            Log.info $"in s_terminate task '{ss.task.id}'"
            ss.error
            |> Option.iter (fun e ->
                ss.task.bus.postOutput (TFo_Error e)
                Log.error (string e)
                ss.cts.CancelAfter(1000))
            Log.info $"s_terminate: message ignored {msg}"
            return !!(s_terminate ss)
        }

    ///construct flow and also start it
    let create task : IFlow<TaskFlowMsgIn> =
        ///initial substate
        let ss0 = SubState.Create task

        //initial state
        let s0 = States.s_start ss0

        //start flow
        Workflow.run ss0.cts.Token task.bus s0

        //return handler to talk to flow
        {new IFlow<TaskFlowMsgIn> with

            member _.Terminate () =
                async {
                    Log.info "terminating flow ..."
                    do! Async.Sleep(1000)
                    ss0.cts.Cancel()
                    ss0.task.bus.Close()
                }
                |> Async.Start

            member _.Post msg = task.bus.PostInput (W_App msg)
        }

