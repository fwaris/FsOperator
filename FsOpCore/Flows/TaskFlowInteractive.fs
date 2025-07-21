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
        | TFi_Voice_AddGuidance of string
        | TFi_Voice_SetUrl of string

    ///flow output messages
    type TaskFlowMsgOut =
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Paused of ChatMsg list
        | TFo_Usage of Map<string,FsResponses.Usage list>
        | TFo_ChatUpdated of ChatMsg list
        | TFo_Done of ChatMsg list
        | TFo_Log of string

    let createVoiceFunctions (driver:IUIDriver) (bus:WBus<_,_>) =
        {
            Functions.gotoUrl = fun t ->
                async {
                    do! driver.start t
                    bus.PostInput(W_App (TFi_Voice_SetUrl t))
                    bus.PostInput(W_App TFi_Start)
                }
            Functions.addGuidance = fun t -> async{bus.PostInput(W_App (TFi_Voice_AddGuidance t))}
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
        voiceModel          : string
        voicePrompt         : string option
    }
        with
            static member Create task voiceAsst voicePrompt =
                {
                    task = task
                    cts = new CancellationTokenSource()
                    corrId = ""
                    cuaLoopCount = 0
                    visualState = None
                    error = None
                    cuaResp = None
                    voiceAsst = voiceAsst
                    voiceModel = RTOpenAI.Api.C.OPENAI_RT_MODEL_GPT4O
                    voicePrompt = voicePrompt
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
            member this.setCuaPrompt t = {this with task.cuaPrompt = t}
            member this.prependCuaMessage m = {this with task.cuaMessages = m::this.task.cuaMessages}

            member this.appendVoiceUsages (r:ResponseDetails) =
                match r.usage with
                | Some u ->
                    let u = Voice.toResponsesUsage u
                    {this with task= this.task.appendUsage (this.voiceModel,u)}
                | None -> this

            member this.performComputerCall resp =
                async{
                    let! t,vstate = Cua.doActionAndSnapshot this.task resp
                    return {this with task = t; visualState=vstate}
                }

            member this.snapshot() =
                async{
                    let! t,vstate = Cua.snapshot this.task
                    return {this with task = t; visualState=Some vstate}
                }

            member this.callFunctions resp =
                async {
                    let! task = Reasoner.callFunctions this.task resp
                    return {this with task = task}
                }



    //state machine
    module States =

        ///Matches if we need to get guidance from reasoner before continuing with CUA
        let (|GetReasonerGuidance|_|) (ss:SubState) msg =
            match msg with
            | W_Cua resp when ss.cuaLoopCount >= MAXC && ss.task.reasonerPrompt.IsSome -> Some resp
            | _                                                                        -> None

        ///Matches if when we should Resume with CUA loop after receiving guidance
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
            | FuncCall (corrId,resp)        -> TxnAsync (
                                                    async {
                                                        let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                                        let! ss = ss.callFunctions resp
                                                        let corrId = Reasoner.getGuidanceForCuaNextAction ss.task ss.task.reasonerPrompt.Value //continue after func. calls
                                                        let ss = ss.setCorrId corrId
                                                        return F(s_reason ss, [TFo_Usage ss.task.usage])
                                                    })
            | _                              -> Cont (ss,[TFo_Usage ss.task.usage],msg)

        ///<summary>
        /// Capture common voice event processing here.<br />
        /// Note: that voice has two concurrent channels, audio and data.<br />
        /// The audio conversation with the user happens concurrently<br />
        /// with the data exchange happening in the background.
        /// </summary>
        and (|Voice|_|) nextState (ss:SubState,msg) =

            match ss.voiceAsst, msg with

            //handle function call from voice assistant
            | Some conn, Voice.FuncCall f -> async{
                                                do! Voice.callFunction conn ss.task.kernel f
                                                return F(nextState ss,[])
                                             } |> Some

            //handle other voice events
            | Some conn, W_Voice ev ->
                match ev with
                | SessionCreated s  -> Voice.sendUpdateSession ss.voicePrompt conn s.session
                                       async {return F(nextState ss, [])} |> Some

                | ResponseDone r    -> let ss = ss.appendVoiceUsages r.response              //capture voice model usage
                                       async {return F(nextState ss, [])} |> Some

                | SessionUpdated s  -> let ss = match s.session.model with Some m -> {ss with voiceModel= m} | _ -> ss
                                       async {return F(nextState ss, [])} |> Some

                //we are ignoring any other voice events
                | _                 -> async {return F(nextState ss, [])} |> Some


            //handle voice related app messages
            |Some _, W_App (TFi_Voice_SetUrl url)  ->
                                      let ss = {ss with task.target = url }
                                      async { return F(nextState ss,[])} |> Some

            //handle voice related app messages
            |Some _, W_App (TFi_Voice_AddGuidance t)  ->
                                      let ss = ss.prependCuaMessage (User t)
                                      async { return F(nextState ss,[TFo_ChatUpdated ss.task.cuaMessages])} |> Some

            //voice event received but no voice assistant configured, log error and continue
            | None, W_Voice msg    -> Log.warn $"{nameof (|Voice|_|)} voice assistant not configured, ignoring {msg}"
                                      async {return F(nextState ss, [])} |> Some

            //not a voice event - dont match
            | _ -> None

        (* --- states --- *)

        and s_start ss msg = async {
            Log.info $"in {nameof s_start} task '{ss.task.id}'"
            match ss,msg with
            | Voice s_start (st)             -> return! st

            | Txn st                         -> return st
            | TxnAsync st                    -> return! st
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
                                                FlResps.postCuaRequest ss.task.bus.PostInput {CuaReq.Default with instructions=(Some ss.task.cuaPrompt); visualState=vs}
                                                return F(s_cua ss,ms)
            | Cont(ss,ms,x)                  -> Log.warn $"{nameof s_start}: expecting TFi_Start, TFi_Prime, got {x}"
                                                return !!(s_start ss)
        }

        and s_cua ss msg = async {
            let ss = ss.incrCuaLoopCount()
            Log.info $"in {nameof s_cua} {ss.cuaLoopCount} task '{ss.task.id}'"
            match ss,msg with
            | Voice s_cua (st)                          -> return! st

            | Txn st                                    -> return st
            | TxnAsync st                               -> return! st

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
            | Voice s_reason (st)                  -> return! st

            | Txn st                               -> return st
            | TxnAsync st                          -> return! st

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
            | Voice s_pause (st)                 -> return! st

            | Txn st                             -> return st
            | TxnAsync st                        -> return! st

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
            | Voice s_summarizing (st)              -> return! st

            | Txn st                                -> return st
            | TxnAsync st                           -> return! st

            | Cont (ss,ms, Reasoner ss.corrId resp) -> let ss,_ = ss.setTask2 (Cua.prependAsstMsg ss.task resp)
                                                       return F(s_terminate ss, TFo_Done ss.task.cuaMessages::ms)
            | Cont (ss,_,x)                         -> return ignoreMsg (s_summarizing ss) x (nameof s_summarizing)
        }

        and s_terminate ss msg = async {
            Log.info $"in s_terminate task '{ss.task.id}'"
            ss.voiceAsst |> Option.iter RTOpenAI.Api.Connection.close
            let ss = {ss with voiceAsst = None}
            ss.error
            |> Option.iter (fun e ->
                ss.task.bus.postOutput (TFo_Error e)
                Log.error (string e)
                ss.cts.CancelAfter(1000))
            Log.info $"s_terminate: message ignored {msg}"
            return !!(s_terminate ss)
        }
    ///construct flow and also start it
    let create task voiceAsst voicePrompt : IFlow<TaskFlowMsgIn> =
        ///initial substate
        let ss0 = SubState.Create task voiceAsst voicePrompt

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

