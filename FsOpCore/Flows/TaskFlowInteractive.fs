namespace FsOpCore
open System
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open RTOpenAI.Api.Events
open FlUtils

module TaskFlowInteractive =    
    ///controls how many CUA turns to do before getting reasoner guidance
    let MAXC = 1 

    ///flow input messages
    type TaskFlowMsgIn =
        | TFi_Prime
        | TFi_Start
        | TFi_Resume of string
        | TFi_EndAndReport        
        | TFi_SetInstructions of string
        | TFi_AddGuidance of string

    ///flow output messages
    type TaskFlowMsgOut =
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Paused of ChatMsg list
        | TFo_Usage of Map<string,FsResponses.Usage list>
        | TFo_ChatUpdated of ChatMsg list
        | TFo_Done of ChatMsg list
        | TFo_Log of string
        | TFo_InstructionsSet of string

    let createVoiceFunctions (task:TaskState<TaskFlowMsgIn,TaskFlowMsgOut>) = 
        {
            Functions.setInstructions = fun t -> async{ task.bus.PostInput (W_App (TFi_SetInstructions t))}
            Functions.gotoUrl = fun t -> task.driver.start t
            Functions.addGuidance = fun t -> async{task.bus.PostInput(W_App (TFi_AddGuidance t))}            
            Functions.startTask = fun () -> async{task.bus.PostInput(W_App TFi_Start)}
        }        

    ///keeps needed local 'substate'
    type SubState = {
        task                : TaskState<TaskFlowMsgIn,TaskFlowMsgOut>
        cts                 : CancellationTokenSource
        visualState         : VisualState option
        corrId              : string
        cuaLoopCount        : int
        error               : WErrorType option
        cuaResp             : FsResponses.Response option
        voiceAsst           : RTOpenAI.Api.Connection option
    }
        with
            static member Create task voiceAsst = 
                {
                    task = task
                    cts = new CancellationTokenSource()
                    corrId = ""
                    cuaLoopCount = 0
                    visualState = None
                    error = None
                    cuaResp = None
                    voiceAsst = voiceAsst
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

        ///matches if we guidance from reasoner before responding to CUA
        let (|GetReasonerGuidance|_|) (ss:SubState) msg = 
            match msg with 
            | W_Cua resp when ss.cuaLoopCount >= MAXC && ss.task.reasonerPrompt.IsSome -> Some resp
            | _                                                                        -> None

        ///resume with CUA loop after receiving guidance
        let (|Resume|_|) msg = 
            match msg with 
            | W_App (TFi_Resume tx) -> Some tx
            | _                     -> None

        ///<summary>
        /// Capture common message processing here.<br />
        /// The patterns with their accepted value are:<br />
        /// -Txn: transition to the returned state<br />
        /// -TxnAync: - async transition to the returned state<br />
        /// -Cont: - no pattern matched but substate may have been updated. Use the new substate and continue matching other patterns.<br />
        /// </summary>
        let rec (|Txn|TxnAsync|Cont|) (ss:SubState,msg) =
            let ss = ss.appendUsage msg
            match msg with 
            | W_Err e                        -> let ss = ss.setError e
                                                Txn(F(s_terminate ss, [TFo_Usage ss.task.usage]))
            | W_App TFi_EndAndReport         -> let corrId = Reasoner.stopAndSummarize ss.task
                                                let ss = ss.setCorrId corrId
                                                Txn(F(s_summarizing ss,[TFo_Usage ss.task.usage]))
            | Cua_FuncCall (resp)            -> TxnAsync (
                                                    async {
                                                        let! fouts = Cua.callFunctions ss.task resp
                                                        let ss = ss.setCuaResponse resp
                                                        Cua.postCuaFuncResults ss.task resp fouts
                                                        return F(s_cua ss,[TFo_Usage ss.task.usage]) 
                                                    })
            | FuncCall ss.corrId (resp)      -> TxnAsync (
                                                    async {
                                                        let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                        let! ss = ss.callFunctions resp
                                                        let corrId = Reasoner.getGuidanceForCuaNextAction ss.task ss.task.reasonerPrompt.Value //continue after func. calls
                                                        let ss = ss.setCorrId corrId
                                                        return F(s_reason ss, [TFo_Usage ss.task.usage])
                                                    })
            | _                              -> Cont (ss,[TFo_Usage ss.task.usage],msg)

        ///<summary>
        ///Capture common voice event processing here.<br />
        ///Note: that voice has two concurrent channels audio and data.<br />
        /// The audio conversation with the user happens concurrently<br />
        /// with the data exchange.
        /// </summary>
        and (|Voice|_|) nextState (ss:SubState,msg) = 
            match msg with 
            | Voice.FuncCall f ->                 
                match ss.voiceAsst with 
                | Some conn ->
                        async{
                            do! Voice.callFunction conn ss.task.kernel f
                            return F(nextState ss,[])
                        }
                        |> Some
                | None -> None
            | _ -> None

        (* --- states --- *)

        and s_start ss msg = async {
            Log.info $"in {nameof s_start} task '{ss.task.id}'"
            match ss,msg with
            | Txn st                         -> return st
            | TxnAsync st                    -> return! st
            | Voice s_start (st)             -> return! st
            | Cont (ss,ms, W_App TFi_Prime)  -> match ss.voiceAsst with 
                                                | None -> 
                                                    ss.task.bus.PostInput (W_App TFi_Start)
                                                    return F(s_start ss,ms)
                                                | Some cnn -> 
                                                    do! Voice.startVoice cnn ss.task
                                                    return F(s_start ss,ms)
            | Cont (ss,ms, W_App TFi_Start)  -> do! ss.task.driver.start ss.task.target
                                                let! vs = snapshot ss.task.driver
                                                let ss = ss.setVisualState (Some vs)
                                                let ss = ss.setTask (ss.task.prependSnapshot vs.snapshot)
                                                FlResps.postStartCua ss.task.bus.PostInput {CuaReq.Default with instructions=(Some ss.task.cuaPrompt); visualState=vs}
                                                return F(s_cua ss,ms)
            | Cont(ss,ms,x)                  -> Log.warn $"{nameof s_start}: expecting {TFi_Start} message to start flow but got {x}"
                                                return !!(s_start ss)
        }
       
        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} task '{ss.task.id}'"
            match ss,msg with
            | Txn st                                    -> return st
            | TxnAsync st                               -> return! st
            | Voice s_cua (st)                        -> return! st

            | Cont (ss,ms, NoComputerCall resp)         -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                           return F(s_pause ss,TFo_Paused ss.task.cuaMessages::ms)
            | Cont (ss,ms, GetReasonerGuidance ss resp) -> //should get guidance from reasoner before responding to CUA
                                                           let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp) 
                                                           let! ss = ss.performComputerCall resp
                                                           let outMsgs = if ss.visualState.IsSome then ss.lastActionM() else []
                                                           let corrId = Reasoner.getGuidanceForCuaNextAction ss.task ss.task.reasonerPrompt.Value
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
            match ss,msg with 
            | Txn st                               -> return st
            | TxnAsync st                          -> return! st
            | Voice s_reason (st)                   -> return! st

            | Cont (ss,ms,Reasoner ss.corrId resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                      match Reasoner.reasonerGuidance resp with
                                                      | None ->
                                                            Log.info $"task {ss.task.id} completed, summarizing"
                                                            let corrId = Reasoner.stopAndSummarize ss.task
                                                            let ss = ss.setCorrId corrId
                                                            return F(s_summarizing ss, TFo_Log "Finalizing"::ms)
                                                      | Some guidance ->
                                                            Cua.postCuaNext ss.task ss.visualState ss.cuaResp.Value (Some guidance)
                                                            return F(s_cua ss,TFo_Log $"Reasonger guidance '{guidance}'"::ms)
            | Cont(ss,_,x)                         -> return ignoreMsg (s_reason ss) x (nameof s_reason)
        }

        and s_pause ss msg = async {
            Log.info $"in {nameof s_pause} task '{ss.task.id}'"
            match ss,msg with 
            | Txn st                             -> return st
            | TxnAsync st                        -> return! st
            | Voice s_pause (st)                 -> return! st

            | Cont (ss,ms, Resume tx)            -> let ss = ss.setTask (ss.task.prependCuaMessage (User tx))
                                                    let! vs = FlUtils.snapshot(ss.task.driver)
                                                    let ss = {ss with task = ss.task.prependSnapshot vs.snapshot}
                                                    let ss = ss.setVisualState (Some vs)
                                                    Cua.postResumeCua ss.task vs //resume chat (note server history is gone so use local history)
                                                    return F(s_cua ss,TFo_ChatUpdated ss.task.cuaMessages::ms)
            | Cont (ss,_,x)                      -> return ignoreMsg (s_pause ss) x (nameof s_pause)
        }                                           

        and s_summarizing ss msg = async {
            Log.info $"in {nameof s_summarizing} task '{ss.task.id}'"
            match ss,msg with 
            | Txn st                                -> return st
            | TxnAsync st                           -> return! st
            | Voice s_summarizing (st)              -> return! st

            | Cont (ss,ms, Reasoner ss.corrId resp) -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                       return F(s_terminate ss, TFo_Done ss.task.cuaMessages::ms)
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
    let create task voiceAsst : IFlow<TaskFlowMsgIn> =        
        ///initial substate
        let ss0 = SubState.Create task voiceAsst

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

