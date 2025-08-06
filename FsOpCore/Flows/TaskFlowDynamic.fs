namespace FsOpCore
open System
open System.Text.Json
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open FlUtils
open FsOpCore.Dynamic

module TaskFlowDynamic =
    ///controls how many CUA turns to do before getting reasoner guidance
    let MAXC = 5

    ///flow input messages
    type TaskFlowMsgIn =
        | TFi_Start
        | TFi_EndAndReport
        | TFi_TerminateTask
        | TFi_GetSteps

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
        stepCorrIds         : (string*DateTime) option
        otherCorrId         : string
        cuaLoopCount        : int
        reasonerLoopCount   : int
        error               : WErrorType option
        cuaResp             : FsResponses.Response option
        pendingRsnrReq      : FsResponses.Request option
    }
        with
            static member Create task =
                let t =
                    {
                        task = task
                        cts = new CancellationTokenSource()
                        stepCorrIds = None
                        otherCorrId = ""
                        cuaLoopCount = 0
                        reasonerLoopCount = 0
                        visualState = None
                        error = None
                        cuaResp = None
                        pendingRsnrReq = None
                    }
                SubState.hookTaskTools t
                t

            member this.setTask v : SubState = {this with task = v} // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this v
            member this.setTask2 (v,x)  = {this with task = v},x // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this
            member this.lastActionM() = this.task.lastAction() |> List.map TFo_Action
            member this.setCorrIdAndReasonserCache id = {this with stepCorrIds = id; task.reasonerItems = []}
            member this.incrReasonerLoopCount()  = {this with reasonerLoopCount = this.reasonerLoopCount + 1}
            member this.incrCuaLoopCount()  = {this with cuaLoopCount = this.cuaLoopCount + 1}
            member this.resetCuaLoopCount() = {this with cuaLoopCount = 0}
            member this.setVisualState vs = {this with visualState = vs}
            member this.setError e = {this with error = Some e}
            member this.setCuaResponse r = {this with cuaResp = Some r}
            member this.addCorrId id = {this with stepCorrIds = Some (id,DateTime.Now) }
            member this.haveStepId id' = this.stepCorrIds |> Option.map (fun (i,_) -> i=id') |> Option.defaultValue false
            member this.removeStepId id = 
                let corrId =  
                    match this.stepCorrIds with 
                    | Some (id',_)  when id=id' -> ()
                    | _ -> Log.warn $"Unmatched corrId {id}";
                {this with stepCorrIds = None}
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
                {this with task = this.task.setSteps newSteps}

            member private this.PostUpdateSteps() =
                let corrId = Reasoner_Dynamic.getNextSteps this.task
                let ss = this.addCorrId corrId
                let ss = ss.incrCuaLoopCount()
                let ss = ss.setTask (ss.task.resetReasonerItems())
                ss

            member this.tryPostUpdateSteps() =
                match this.stepCorrIds with 
                | None -> this.PostUpdateSteps()
                | Some (id,dt) when (DateTime.Now - dt).TotalSeconds > 60.0 -> this.PostUpdateSteps()
                | _ ->                 
                    Log.info $"CorrIds: {this.stepCorrIds}"
                    this //there are already messages in play so wait but keep collecting items for reasoner next call

            member this.delayPostGetSteps() =     
                async {
                    do! Async.Sleep 10000                           //delay and post get steps to keep reasoner loop alive
                    this.task.bus.PostInput (W_App TFi_GetSteps)
                }
                |> Async.Start

            member this.postCuaNext resp =
                Cua_Dynamic.postCuaNextStep this.task this.visualState resp
                this.setTask (this.task.resetCuaItems())

            static member hookTaskTools (ss:SubState) =
                let tasktools = ss.task.kernel.GetRequiredService<Functions.FsOpTaskTools>()
                tasktools.SetFunctions({Functions.TaskToolImpl.taskDone = fun ()->async{                    
                    Log.info $"Done call for task {ss.task.id}"
                    ss.task.bus.PostInput (W_App TaskFlowMsgIn.TFi_TerminateTask)}}) //wire task tools plugin to this instance

    //state machine
    module States =

        let getInitialSteps (ss:SubState) =
            let id = Reasoner_Dynamic.getInitialSteps ss.task
            ss.addCorrId id

        ///with the given correlation id
        let (|Reasoner|_|) corrId msg =
            match msg with
            | W_Reasoner (id,resp) when id = corrId -> Some resp
            | _                                     -> None

        ///matches if reasoner response contains steps
        let (|GotSteps|_|) (ss:SubState) msg =
            match msg with
            | W_Reasoner (id,resp) when ss.haveStepId id  ->
                    let ss = ss.removeStepId id
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
                    | None -> 
                        Log.warn "Empty steps"
                        Some ss
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
                                                        return (F(s_terminate ss, [TFo_Error e; TFo_Usage ss.task.usage]))
                                                    })
            | W_App TFi_EndAndReport         -> Txn (async {
                                                        let corrId = Reasoner.stopAndSummarizeStep ss.task
                                                        let ss = ss.appendUsage msg
                                                        let ss = ss.setOtherCorrId corrId
                                                        return F(s_summarizing ss,[TFo_Usage ss.task.usage])
                                                    })
            | W_App TFi_TerminateTask        -> Txn (async {
                                                        let ss = ss.appendUsage msg
                                                        return F(s_terminate ss,[TFo_Done ss.task; TFo_Usage ss.task.usage])
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
                                                let ss = getInitialSteps ss
                                                do! ss.task.driver.start ss.task.target
                                                let! vs = snapshot ss.task.driver
                                                let ss = ss.setVisualState (Some vs)
                                                let ss = ss.setTask (ss.task.prependSnapshot vs.snapshot)
                                                Cua_Dynamic.startStep ss.visualState.Value ss.task
                                                return F(s_cua ss,ms)   //start processing steps
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss
                                                Log.warn $"{nameof s_start}: expecting TFi_Start but got {x}"
                                                return F(s_start ss,ms)
        }

        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} {ss.reasonerLoopCount} task '{ss.task.id}'"
            match s_pause,ss,msg with
            | Txn st                         -> return! st
            | Cont (GotSteps ss ss',ssa)     -> let! ss',ms = ssa ss'
                                                return F(s_cua ss',ms)
            | Cont (NoComputerCall resp,ssa) -> let! ss,ms = ssa ss
                                                let ss,t = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                Log.info $"no computer call got '{t}'"
                                                let ss = ss.tryPostUpdateSteps()
                                                ss.delayPostGetSteps()
                                                return F(s_pause ss,ms)
            | Cont (W_Cua resp,ssa)          -> let! ss,ms = ssa ss
                                                match ss.task.NextToDo() with
                                                | Choice2Of2 None -> return F(s_terminate ss,TFo_Done ss.task::ms)
                                                | Choice1Of2 _  //no steps received yet
                                                | Choice2Of2 (Some _ ) ->
                                                    let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp) //capture any msg from CUA
                                                    let! ss = ss.doActionAndSnapshot resp
                                                    let ss = ss.postCuaNext resp
                                                    let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                    let ss = ss.tryPostUpdateSteps()
                                                    return F(s_cua ss,outMsgs @ ms) //increment count
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss
                                                return ignoreMsg (s_cua ss) x (nameof s_pause)
        }

        and s_pause (ss:SubState) msg = async {
            let ss = ss.resetCuaLoopCount()            
            Log.info $"in {nameof s_pause} {ss.cuaLoopCount} {ss.reasonerLoopCount} task '{ss.task.id}'"
            match s_pause,ss,msg with
            | Txn st                         -> return! st
            | Cont (W_App TFi_GetSteps,ssa)  -> let! ss,ms = ssa ss
                                                let ss = ss.tryPostUpdateSteps()
                                                ss.delayPostGetSteps()
                                                return F(s_pause ss,ms)
            | Cont (GotSteps ss ss',ssa)     -> let! ss',ms = ssa ss'
                                                match ss'.task.NextToDo() with 
                                                | Choice2Of2 None -> return F(s_terminate ss',ms)               //nothing to do terminate
                                                | _               -> 
                                                    let! vs = snapshot ss.task.driver                           //restart CUA loop with new steps
                                                    let ss = ss.setVisualState (Some vs)
                                                    let ss = ss.setTask (ss.task.prependSnapshot vs.snapshot)
                                                    Cua_Dynamic.startStep ss.visualState.Value ss.task
                                                    let ss = ss.tryPostUpdateSteps()
                                                    return F(s_cua ss,ms)   //start processing steps
            | Cont(x,ssa)                    -> let! ss,ms = ssa ss
                                                return ignoreMsg (s_pause ss) x (nameof s_pause)
        }

        and s_summarizing (ss:SubState) msg = async {
            Log.info $"in {nameof s_summarizing} {ss.cuaLoopCount} {ss.reasonerLoopCount} task '{ss.task.id}'"
            match s_summarizing,ss,msg with
            | Txn st                                  -> return! st
            | Cont (Reasoner ss.otherCorrId resp,ssa) -> let! ss,ms = ssa ss
                                                         let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                         return F(s_terminate ss, TFo_Done ss.task::ms)
            | Cont(x,ssa)                             -> let! ss,ms = ssa ss
                                                         return ignoreMsg (s_summarizing ss) x (nameof s_summarizing)
        }

        and s_terminate ss msg = async {
            Log.info $"in s_terminate task {ss.cuaLoopCount} {ss.reasonerLoopCount} '{ss.task.id}'"
            ss.cts.CancelAfter(1000)
            ss.error
            |> Option.iter (fun e ->
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

