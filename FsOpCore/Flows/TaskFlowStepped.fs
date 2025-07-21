namespace FsOpCore
open System
open System.Text.Json
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
        stepCorrIds         : Set<string>
        otherCorrId         : string
        cuaLoopCount        : int
        error               : WErrorType option
        cuaResp             : FsResponses.Response option
        mutable isDone      : bool
    }
        with
            static member Create task =
                let t = 
                    {
                        task = task
                        cts = new CancellationTokenSource()
                        stepCorrIds = Set.empty
                        otherCorrId = ""
                        cuaLoopCount = 0
                        visualState = None
                        error = None
                        cuaResp = None
                        isDone = false
                    }
                SubState.hookTaskTools t
                t

            static member hookTaskTools (ss:SubState) =
                let tasktools = ss.task.kernel.GetRequiredService<Functions.FsOpTaskTools>()
                tasktools.SetFunctions({Functions.TaskToolImpl.taskDone = fun ()->async{ss.isDone<-true}}) //wire task tools plugin to this instance

            member this.setTask v : SubState = {this with task = v} // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this v
            member this.setTask2 (v,x)  = {this with task = v},x // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this
            member this.lastActionM() = this.task.lastAction() |> List.map TFo_Action
            member this.setCorrIdAndReasonserCache id = {this with stepCorrIds = id; task.reasonerItems = []}
            member this.incrCuaLoopCount()  = {this with cuaLoopCount = this.cuaLoopCount + 1}
            member this.resetCuaLoopCount() = {this with cuaLoopCount = 0}
            member this.setVisualState vs = {this with visualState = vs}
            member this.setError e = {this with error = Some e}
            member this.setCuaResponse r = {this with cuaResp = Some r}
            member this.addCorrId id = {this with stepCorrIds = Set.add id this.stepCorrIds}
            member this.removeCorrId id = {this with stepCorrIds = Set.remove id this.stepCorrIds}
            member this.setOtherCorrId id = {this with otherCorrId = id}

            member this.appendUsage = function 
                | W_Cua resp 
                | W_Reasoner (_, resp) -> {this with task = FlUtils.getUsage resp |> this.task.appendUsage} 
                | _ -> this

            member this.doActionAndSnapshot resp =
                async{
                    let! t,vstate = Cua.doActionAndSnapshot this.task resp
                    return {this with task = t; visualState=vstate}
                }

            member this.snapshot()  =
                async{                    
                    let! t,vstate = Cua.snapshot this.task
                    return {this with task = t; visualState= Some vstate}
                }

            member this.callFunctions resp =
                async {
                    let! task = Reasoner.callFunctions this.task resp
                    return {this with task = task}
                }

            member this.updateSteps (newSteps:CuaInstructionStep list) = 
                let steps = 
                    if this.task.steps.steps.IsEmpty then 
                        newSteps |> List.map CuaStep.Create
                    else                   
                        let m = newSteps |> List.map (fun x -> x.step_num,x) |> Map.ofList
                        this.task.steps.steps 
                        |> List.map (fun y -> 
                            m
                            |> Map.tryFind y.step.step_num 
                            |> Option.map (fun s -> {y with step = s})
                            |> Option.defaultValue y)
                {this with task.steps.steps = steps}

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
                let ss = ss.addCorrId id
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
            | W_Reasoner (id,resp) when ss.stepCorrIds.Contains id ->
                    let text = RUtils.outputText resp
                    try 
                        let s = Text.Json.JsonSerializer.Deserialize<CuaInstructions>(text, FlUtils.openAIResponseSerOpts) 
                        let ss' = ss.updateSteps s.steps
                        let sMap = ss'.task.steps.steps |> List.map (fun x-> x.step.step_num,  x.step) |> Map.ofList
                        s.steps 
                        |> List.iter (fun s -> 
                            match sMap |> Map.tryFind s.step_num with 
                            | Some s' when s<>s' -> Log.info $"{s.step_num}: {s.step_instructions}"  //log changes
                            | _                  -> ()
                        )
                        let sMap = s.steps |> List.map(fun x->x.step_num, x) |> Map.ofList
                        
                        s.steps |> List.iter (fun x -> Log.info $"{x.step_num}: {x.step_instructions}")
                        let ss =  ss.updateSteps s.steps
                        let ss = ss.removeCorrId id
                        Some ss
                    with ex -> 
                        Log.exn(ex,nameof GotSteps)
                        Some ss
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
                                                let ss = ss.setOtherCorrId corrId
                                                Txn(F(s_summarizing ss,[TFo_Usage ss.task.usage]))
            | Cua_FuncCall (resp)            -> TxnAsync (
                                                    async {
                                                        let! fouts = Cua.callFunctions ss.task resp
                                                        let ss = ss.setCuaResponse resp
                                                        Cua.postCuaFuncResults  ss.task resp fouts
                                                        return F(s_ret ss,[TFo_Usage ss.task.usage])
                                                    })
            | FuncCall (corrId,resp)         -> TxnAsync (
                                                    async {
                                                        let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                        let! ss = ss.callFunctions resp
                                                        let ss = ss.setTask (ss.task.setPrevId resp.id)
                                                        Reasoner.postFunctionCall corrId ss.task
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
            | Cont (ss,ms,GotSteps ss ss')   -> ss'.task.bus.PostInput (W_App TFi_Step)
                                                return F(s_step ss',ms)
            | Cont(ss,ms,x)                  -> Log.warn $"{nameof s_start}: expecting TFi_Start or W_Reasoner with steps message to start flow but got {x}"
                                                return F(s_start ss,ms)
        }

        and s_step ss msg = async {
            Log.info $"in {nameof s_step} task '{ss.task.id}'"
            match s_step,ss,msg with
            | Txn st                            -> return st
            | TxnAsync st                       -> return! st
            | Cont (ss,ms,GotSteps ss xs)       -> return F(s_step ss,ms)
            | Cont (ss,ms,W_App TFi_Step) when ss.isDone    
                                                -> return F(s_terminate ss,TFo_Done ss.task::ms)
            | Cont (ss,ms,W_App TFi_Step)       -> match ss.task.steps.NextToDo() with
                                                   | Some step ->
                                                       Log.info $"step {step.step.step_num}: {step.step.step_instructions |> shorten 120}"
                                                       let ss = {ss with task.steps = ss.task.steps.SetCurrentStep step.step.step_num}
                                                       let ss = ss.setTask (ss.task.clearReasonerHistory())  //reset reasoner state for each new 'step' in task
                                                       let! ss = ss.snapshot()
                                                       Cua.startStep ss.visualState.Value ss.task
                                                       return F(s_cua ss,ms)
                                                   | None ->
                                                       return F(s_terminate ss,TFo_Done ss.task::ms)
            | Cont (ss,_,x)                     -> return ignoreMsg (s_step ss) x (nameof s_step)
        }

        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} task '{ss.task.id}'"
            match s_cua,ss,msg with
            | Txn st                            -> return st
            | TxnAsync st                       -> return! st
            | Cont (ss,ms,GotSteps ss ss')      -> return F(s_cua ss',ms)
            | Cont (ss,ms, NoComputerCall resp) -> let ss,t = ss.setTask2 (Cua.stepPrependAsstMsg ss.task resp)
                                                   Log.info $"no computer call got '{t}'"
                                                   let ss = {ss with task.steps = ss.task.steps.AdvanceStep()}
                                                   let ss = ss.resetCuaLoopCount()
                                                   ss.task.bus.PostInput (W_App TFi_Step) //start next step, if any
                                                   return F(s_step ss,ms)
            | Cont (ss,ms, W_Cua resp)          -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp) //msg from CUA
                                                   let! ss = ss.doActionAndSnapshot resp
                                                   let ss = ss.setCuaResponse resp
                                                   Cua.postCuaNext ss.task ss.visualState resp None
                                                   let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                   let corrId = Reasoner.postUpdateSteps ss.task
                                                   let ss = ss.addCorrId corrId
                                                   return F(s_cua ss,outMsgs @ ms) //increment count
            | Cont (ss,_,x)                     -> return ignoreMsg (s_cua ss) x (nameof s_cua)
        }

        and s_summarizing (ss:SubState) msg = async {
            Log.info $"in {nameof s_summarizing} task '{ss.task.id}'"
            match s_summarizing,ss,msg with
            | Txn st                                -> return st
            | TxnAsync st                           -> return! st
            | Cont (ss,ms, Reasoner ss.otherCorrId resp) 
                                                    -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
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

