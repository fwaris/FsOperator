namespace FsOpCore
open System
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open FlUtils

module PlanFlowInteractive =    
    ///controls how many CUA turns to do before getting reasoner guidance
    let MAXC = 1 

    ///flow input messages
    type PlanFLowMsgIn =
        | TFi_Start
        | TFi_Resume of string
        | TFi_EndAndReport

    ///flow output messages
    type PlanFLowMsgOut =
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Paused of ChatMsg list
        | TFo_ChatUpdated of ChatMsg list
        | TFo_Done of ChatMsg list

    type SubState = {
        cts          : CancellationTokenSource
        task         : TaskState<PlanFLowMsgIn,PlanFLowMsgOut>
    }
        with
            member this.setTask v : SubState = {this with task = v} // TaskState<PlanFLowMsgIn,PlanFLowMsgOut>.upd this v

            member this.performComputerCall resp = 
                async{
                    let! t,vstate = Cua.performComputerCall this.task resp
                    return {this with task = t},vstate
                }

            member this.callFunctions resp = 
                async {
                    let! task = Reasoner.callFunctions this.task resp
                    return {this with task = task}
                }


    //state machine
    module States =

        (* --- states --- *)

        let rec s_start ss msg = async {
            Log.info $"in {nameof s_start} {ss.task.id}"
            match msg with
            | W_Err e         -> return !!(s_terminate ss (Some e))
            | W_App TFi_Start -> let! (snapshot,w,h,url,env) as sn = snapshot ss.task.driver
                                 let ss = ss.setTask (ss.task.prependSnapshot snapshot)
                                 FlResps.postStartCua ss.task.bus.PostInput {CuaReq.Default with instructions=(Some ss.task.cuaPrompt); snapshot=sn}
                                 return !!(s_cua ss 1)
            | x               -> Log.warn $"{nameof s_start}: expecting {TFi_Start} message to start flow but got {x}"
                                 return !!(s_start ss)
        }

        and s_cua ss count msg = async {
            Log.info $"in {nameof s_cua} {count} {ss.task.id}"
            match msg with
            | W_Err e                        -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport         -> let corrId = Reasoner.stopAndSummarize ss.task
                                                return !!(s_summarizing ss corrId)
            | Cua_FuncCall (resp)            -> let! fouts = Cua.callFunctions ss.task resp
                                                Cua.postCuaFuncResults ss.task resp fouts
                                                return !!(s_cua ss count)
            | W_Cua resp when noCC resp      -> let ss = ss.setTask (Cua.prependAsstMsg ss.task resp) //cua not asking for comptuer call
                                                let corrId = Reasoner.getGuidanceAfterCuaPause ss.task //ask reasoner what to do next
                                                return !!(s_pause ss corrId)
            | W_Cua resp when count >= MAXC  -> let ss = ss.setTask (Cua.prependAsstMsg ss.task resp) //set reasoner guidance for cua
                                                let! ss,visualState = ss.performComputerCall resp
                                                let outMsgs = if visualState.IsSome then [TFo_Action (ss.task.actionsString())] else []
                                                if ss.task.reasonerPrompt.IsSome then  //get reasoner guidance if prompt set
                                                     let corrId = Reasoner.getGuidanceForCuaNextAction ss.task ss.task.reasonerPrompt.Value
                                                     return F(s_reason ss (visualState,resp) corrId,outMsgs)
                                                else
                                                     Cua.postCuaNext ss.task visualState resp None
                                                     return F(s_cua ss count,outMsgs)
            | W_Cua resp                     -> let ss = ss.setTask (Cua.prependAsstMsg ss.task resp) //cont. w/out rsnr guidance
                                                let! ss,visualState = ss.performComputerCall resp            
                                                Cua.postCuaNext ss.task visualState resp None
                                                let outMsgs = if visualState.IsSome then [TFo_Action (ss.task.actionsString())] else []
                                                return F(s_cua ss (count + 1),outMsgs) //increment count 
            | x                              -> return ignoreMsg (s_cua ss count) x (nameof s_cua)
        }

        and s_reason ss (vs,cuaResp) corrId msg  = async {
            Log.info $"in {nameof s_reason} {ss.task.id}"
            match msg with
            | W_Err e                -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport -> let corrId = Reasoner.stopAndSummarize ss.task
                                        return !!(s_summarizing ss corrId)
            | FuncCall corrId (resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                        let! ss = ss.callFunctions resp
                                        let corrId = Reasoner.getGuidanceForCuaNextAction ss.task ss.task.reasonerPrompt.Value //continue after func. calls
                                        return !!(s_reason ss (vs,cuaResp) corrId)
            | Reasoner corrId (resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                        match Reasoner.reasonerGuidance resp with
                                        | None ->
                                                Log.info $"task {ss.task.id} completed"
                                                return F(s_terminate ss None, [TFo_Done ss.task.cuaMessages])
                                        | Some guidance ->
                                                Cua.postCuaNext ss.task vs cuaResp (Some guidance)
                                                return !!(s_cua ss 1)
            | x                      -> return ignoreMsg (s_reason ss (vs,cuaResp) corrId) x (nameof s_reason)
        }

        and s_pause ss corrId msg = async {
            Log.info $"in {nameof s_pause} {ss.task.id}"
            match msg with
            | W_Err e                -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport -> let corrId = Reasoner.stopAndSummarize ss.task
                                        return !!(s_summarizing ss corrId)
            | W_App (TFi_Resume tx)  -> let ss = ss.setTask (ss.task.prependCuaMessage (User tx))
                                        let! (sn,_,_,_,_) as snapshot = FlUtils.snapshot(ss.task.driver)
                                        let ss = {ss with task = ss.task.prependSnapshot sn}
                                        Cua.postResumeCua ss.task snapshot //resume chat (note server history is gone so use history)
                                        return !!(s_cua ss 1)
            | FuncCall corrId (resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                        let! ss = ss.callFunctions resp
                                        let corrId = Reasoner.getGuidanceAfterCuaPause ss.task
                                        return !!(s_pause ss corrId)
            | Reasoner corrId (resp) -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                        match Reasoner.reasonerGuidance resp with
                                        | None ->
                                                Log.info $"task {ss.task.id} completed"
                                                return F(s_terminate ss None, [TFo_Done ss.task.cuaMessages])
                                        | Some guidance ->
                                                ss.task.bus.PostInput (W_App (TFi_Resume guidance))
                                                return !!(s_pause ss "")

            | x                       -> return ignoreMsg (s_pause ss corrId) x (nameof s_pause)
        }

        and s_summarizing ss corrId msg = async {
            Log.info $"in {nameof s_summarizing} {ss.task.id}"
            match msg with
            | W_Err e                 -> return !!(s_terminate ss (Some e))
            | FuncCall corrId (resp)  -> let ss = ss.setTask (ss.task.resetReasonerState resp.id)
                                         let! task = Reasoner.callFunctions ss.task resp
                                         let ss = {ss with task = task}
                                         let corrId = Reasoner.stopAndSummarize ss.task
                                         return !!(s_summarizing ss corrId)
            | Reasoner corrId (resp)  -> let ss = ss.setTask (Cua.prependAsstMsg ss.task resp)
                                         return F(s_terminate ss None, [TFo_Done ss.task.cuaMessages])
            | x                       -> return ignoreMsg (s_summarizing ss corrId) x (nameof s_summarizing)
        }

        and s_terminate ss (e:WErrorType option) msg = async {
            Log.info $"in s_terminate {ss.task.id}"
            e
            |> Option.iter (fun e ->
                ss.task.bus.postOutput (TFo_Error e)
                Log.error (string e)
                ss.cts.CancelAfter(1000))
            Log.info $"s_terminate: message ignored {msg}"
            return !!(s_terminate ss None)
        }

    ///construct flow and also start it
    let create task : IFlow<PlanFLowMsgIn> =
        ///initial substate
        let ss0 = {
            cts=new CancellationTokenSource()
            task = task
        }

        //starting state node
        let s0 = States.s_start ss0

        //start flow
        Workflow.run ss0.cts.Token task.bus s0

        //return handler to talk to flow
        {new IFlow<PlanFLowMsgIn> with

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

