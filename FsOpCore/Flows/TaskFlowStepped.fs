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
        pendingRsnrReq      : FsResponses.Request option
        isDone              : Ref<bool>
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
                        pendingRsnrReq = None
                        isDone = ref false
                    }
                SubState.hookTaskTools t
                t

            static member hookTaskTools (ss:SubState) =
                let tasktools = ss.task.kernel.GetRequiredService<Functions.FsOpTaskTools>()
                tasktools.SetFunctions({Functions.TaskToolImpl.taskDone = fun ()->async{
                    Log.info $"Done call for task {ss.task.id}"
                    ss.isDone.Value <- true}}) //wire task tools plugin to this instance

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

            member this.callCuaFunctions resp =
                async {
                    let! task = Cua.callFunctions this.task resp
                    return {this with task = task}
                }

            member this.updateSteps (newSteps:CuaInstructionStep list) = 
                {this with task.steps = {steps = newSteps}}

            member this.postUpdateSteps() = 
                if this.stepCorrIds.IsEmpty |> not then 
                    this //there are already messages in play so wait
                else
                    let corrId = Reasoner.postUpdateSteps this.task 
                    let ss = this.addCorrId corrId 
                    let ss = ss.setTask (ss.task.resetReasonerItems())
                    ss

            member this.postCuaNext resp = 
                Cua.postCuaNextStep this.task this.visualState resp
                this.setTask (this.task.resetCuaItems())


    //state machine
    module States =

        let getOrGenerateSteps (ss:SubState) =
            if ss.task.steps.steps.IsEmpty then 
                let id = Reasoner.breakTaskIntoSteps ss.task
                let ss = ss.addCorrId id
                Choice1Of2 ss  //no steps yet, getting them
            else
                Choice2Of2 ss //have steps

        ///with the given correlation id
        let (|Reasoner|_|) corrId msg =
            match msg with
            | W_Reasoner (id,resp) when id = corrId -> Some resp
            | _                                     -> None

        ///matches if reasoner response contains steps
        let (|GotSteps|_|) (ss:SubState) msg =
            match msg with
            | W_Reasoner (id,resp) when ss.stepCorrIds.Contains id ->
                    let ss = ss.removeCorrId id
                    let ss = ss.setTask (ss.task.setPrevId resp.id)
                    let text = RUtils.outputText resp
                    match checkEmpty text with 
                    | Some text -> 
                        try 
                            let s = JsonSerializer.Deserialize<CuaInstructions>(text, FlUtils.openAIResponseSerOpts) 
                            let ss = ss.updateSteps s.steps
                            let s2 = formatJson s + Environment.NewLine
                            prependToFile s2 @"c:\s\cua\step.txt"
                            Some ss
                        with ex -> 
                            Log.exn(ex,nameof GotSteps)
                            Some ss
                    | None -> Some ss
            | _ ->  None


        /// <summary>
        /// Active pattern to handle common state processing. Match results:<br />
        /// - Txn state: Transition to returned state (usually for Error and timeout conditions)<br />
        /// - Cont (msg,ssa): Continue processing message but evaluate async function 'ssa'<br />
        /// to get new substate and any output messages
        /// </summary>
        let rec (|Txn|Cont|)  (s_ret,ss:SubState,msg) =
            match msg with
            | W_Err e                        -> Txn (async {
                                                        let ss = ss.appendUsage msg
                                                        let ss = ss.setError e                                                
                                                        return (F(s_terminate ss, [TFo_Usage ss.task.usage]))
                                                    })
            | W_App TFi_EndAndReport         -> Txn (async {
                                                        let corrId = Reasoner.stopAndSummarizeStep ss.task
                                                        let ss = ss.appendUsage msg
                                                        let ss = ss.setOtherCorrId corrId
                                                        return F(s_summarizing ss,[TFo_Usage ss.task.usage])
                                                    })
            | Cua_FuncCall (resp)            -> Cont (msg,
                                                      fun (ss:SubState) -> async {
                                                        let ss = ss.appendUsage msg
                                                        let! ss = ss.callCuaFunctions resp     //just put the function call results in cuaItems 
                                                        return (ss,[TFo_Usage ss.task.usage])
                                                    })
            | FuncCall (corrId,resp)         -> Cont(msg,
                                                    fun (ss:SubState) -> async {
                                                        let ss = ss.appendUsage msg
                                                        let! ss = ss.callFunctions resp         //just put the function call results in reasonerItems
                                                        return (ss,[TFo_Usage ss.task.usage])
                                                    })
            | _                              -> Cont (msg,
                                                      fun ss -> async {
                                                        let ss = ss.appendUsage msg
                                                        return (ss,[TFo_Usage ss.task.usage]) 
                                                      })
        (* --- states --- *)

        and s_start ss msg = async {
            Log.info $"in {nameof s_start} task '{ss.task.id}'"
            let fn = @"c:\s\cua\step.txt"
            if System.IO.File.Exists(fn) then System.IO.File.Delete(fn)
            match s_start,ss,msg with
            | Txn st                         -> return! st
            | Cont (W_App TFi_Start,ssa)     -> let! ss,ms = ssa ss
                                                let ss' = getOrGenerateSteps ss
                                                do! ss.task.driver.start ss.task.target
                                                match ss' with
                                                | Choice1Of2 ss -> return F(s_start ss,ms)  //no steps yet, need to wait for 'GotSteps'
                                                | Choice2Of2 ss -> ss.task.bus.PostInput (W_App TFi_Step)
                                                                   return F(s_step ss,ms)   //start processing steps
            | Cont (GotSteps ss ss',ssa)     -> let! ss',ms = ssa ss'
                                                ss'.task.bus.PostInput (W_App TFi_Step)
                                                return F(s_step ss',ms)
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss 
                                                Log.warn $"{nameof s_start}: expecting TFi_Start or W_Reasoner with steps message to start flow but got {x}"
                                                return F(s_start ss,ms)
        }

        and s_step ss msg = async {
            Log.info $"in {nameof s_step} task '{ss.task.id}'"
            match s_step,ss,msg with
            | Txn st                         -> return! st
            | Cont (GotSteps ss ss',ssa)     -> let! ss',ms = ssa ss'
                                                ss'.task.bus.PostInput (W_App TFi_Step)
                                                return F(s_step ss',ms)
            | Cont (W_App TFi_Step,ssa)  when ss.isDone.Value
                                             -> let! ss,ms = ssa ss
                                                return F(s_terminate ss,TFo_Done ss.task::ms)
            | Cont (W_App TFi_Step,ssa)      -> let! ss,ms = ssa ss
                                                match ss.task.steps.NextToDo() with
                                                | Some step ->
                                                    Log.info $"ToDo: {step.step_instructions |> shorten 120}"                                                    
                                                    let! ss = ss.snapshot()
                                                    Cua.startStep ss.visualState.Value ss.task
                                                    return F(s_cua ss,ms)
                                                | None ->
                                                       return F(s_terminate ss,TFo_Done ss.task::ms)
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss 
                                                return ignoreMsg (s_step ss) x (nameof s_step)
        }

        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} task '{ss.task.id}'"
            match s_step,ss,msg with
            | Txn st                         -> return! st
            | Cont (GotSteps ss ss',ssa)     -> let! ss',ms = ssa ss'                                                
                                                return F(s_cua ss',ms)

            | Cont (NoComputerCall resp,ssa) -> let! ss,ms = ssa ss
                                                let ss,t = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                Log.info $"no computer call got '{t}'"
                                                let ss = ss.postUpdateSteps()
                                                ss.task.bus.PostInput (W_App TFi_Step) //start next step, if any
                                                return F(s_step ss,ms)
            | Cont (W_Cua resp,ssa)          -> let! ss,ms = ssa ss
                                                let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp) //capture any msg from CUA
                                                let! ss = ss.doActionAndSnapshot resp
                                                let ss = ss.postCuaNext resp
                                                let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                let ss = ss.postUpdateSteps()
                                                return F(s_cua ss,outMsgs @ ms) //increment count
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss 
                                                return ignoreMsg (s_cua ss) x (nameof s_step)
        }

        and s_summarizing (ss:SubState) msg = async {
            Log.info $"in {nameof s_summarizing} task '{ss.task.id}'"
            match s_summarizing,ss,msg with
            | Txn st                                  -> return! st
            | Cont (Reasoner ss.otherCorrId resp,ssa) -> let! ss,ms = ssa ss 
                                                         let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                         return F(s_terminate ss, TFo_Done ss.task::ms)
            | Cont(x,ssa)                             -> let! ss,ms = ssa ss 
                                                         return ignoreMsg (s_summarizing ss) x (nameof s_summarizing)
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

