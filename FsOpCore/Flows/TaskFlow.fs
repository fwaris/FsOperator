namespace FsOpCore
open System.Threading
open System.Threading.Channels
open FsResponses
open FlUtils

module TaskFlow =
    let MAX_SNAPSHOTS = 3
    
    ///flow input messages
    type TaskFLowMsgIn =
        | TFi_Start 
        | TFi_Resume of string
        | TFi_ChatUpdated of Chat
        | TFi_EndAndReport

    ///messages output by flow
    type TaskFLowMsgOut =
        | TFo_Paused 
        | TFo_ChatUpdated of Chat
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Summary of Chat

    type SubState = {
        cts         : CancellationTokenSource
        chat        : Chat
        driver      : IUIDriver       
        bus         : WBus<TaskFLowMsgIn,TaskFLowMsgOut>
        snapshots   : string list
        actions     : string list
    }
        with 
            member this.appendSnapshot s = {this with snapshots = s::this.snapshots |> List.truncate MAX_SNAPSHOTS }
            member this.appendMsg msg = {this with chat = Chat.append msg this.chat}
            member this.appendAction a = {this with actions = a::this.actions |> List.truncate MAX_SNAPSHOTS }
            member this.setPrompt b = {this with chat = Chat.setPrompt b this.chat}

    ///handle Cua respones to potentially perform a computer call
    let performCall (ss:SubState) resp = async {
        //process computer call
        let! (ss,outMsgs,visualState) = 
            match FlResps.computerCall resp with
            | Some cb -> 
                async {
                    do! Actions.doAction 2 ss.driver cb.action
                    let! visualState = snapshot ss.driver
                    let (snapshot,w,h,url,env) = visualState
                    let ss = ss.appendSnapshot snapshot      
                    let actStr = Actions.actionToString cb.action
                    let ss = ss.appendAction actStr
                    return ss,[(TFo_Action actStr)],Some visualState
                }
            | None -> async{ return ss,[],None }

        return ss,outMsgs,visualState
    }

    let summarizationPrompt taskInstructions = $"""The user has tasked an automated 'computer assistant'
to accomplish a task as given in the TASK INSTRUCTIONS below. The computer
assistant has operated the computer in pursuit of the task. Along the way it has
taken some screenshots. Give any available message history and the screenshots, summarize the content
obtained thus far, in relation to the task instructions.

# TASK INSTRUCTIONS
{taskInstructions}
"""

    let reasonerPrompt cuaInstructions (cuaActions:string list) = $"""
The 'computer use agent' (CUA) model is given instructions [CUA_INSTRUCTIONS] to accomplish a task.

CUA 'looks' at screenshots and issues computer commands such as, 'click', 'move', 'type text', etc. to achieve its goal.
However the CUA model is not good at following instructions sometimes. 
Look at the message history (including the screenshots); [ACTION_HISTORY]; and generate additional guidance that 
may be provided to the CUA model *after* the current given command has been performed and *before* CUA is ready to generate the next command.

Note: Sometimes CUA has trouble performing scrolling using the simple 'scroll' command. If CUA seems stuck,
suggest alternative scroll commands e.g. 'wheel' and PAGEUP/PAGEDOWN keystrokes.

When asking CUA to enter text, suggest type <text> in the <field name>


[CUA_INSTRUCTIONS]
{cuaInstructions}

[ACTION_HISTROY]
{cuaActions |> List.rev |> String.concat ", "}
"""
//Just give the immediate next step to follow. Dont' give multi-step instructions. 

//BE BRIEF

    let postToReasoner correlationId (ss:SubState) reasonerInstructions  =
        async {
            try
                let screenshots = ss.snapshots |> List.rev
                let messages = ss.chat.messages |> FlResps.toMessages |> FlResps.truncateHistory 
                let imgs = screenshots |> List.map(fun i -> Content.Input_image {|image_url=i|})
                let msg = {Message.Default with content = imgs}//contImgs}
                let chatHistory = messages |> List.map InputOutputItem.Message
                let msgInput = InputOutputItem.Message msg
                let req = {Request.Default with
                                    input = chatHistory @ [msgInput];
                                    instructions = reasonerInstructions
                                    store = false
                                    model=Models.gpt_41
                                    truncation = Some Truncation.auto
                                    metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                                }
                do! FlResps.sendRequest Workflow.ReasonerMsgWithCorrId ss.bus.PostInput req                                    
            with ex ->
                Log.exn(ex,"summarizeProgressReasoner")
                return raise ex
        }
        |> FlResps.catch ss.bus.PostInput


    ///if the cua model is not able to produce a summary, use the reasoner model to do the same, as a fallback
    let postSummarizeProgress (ss:SubState) = 
        let id = newId()
        ss.chat.systemMessage
        |> Option.map summarizationPrompt
        |> postToReasoner id ss 
        id

    let postGetRsnrGuidanceForCua ss = 
        let id = newId()
        Some(reasonerPrompt ss.chat.systemMessage ss.actions)
        |> postToReasoner id ss
        id

    ///returns true if no compter call present
    let noCC resp = 
        let cc = FlResps.computerCall resp        
        cc |> Option.isNone

    ///if there is text content in resp then update substate chat and post updated chat message
    let emitText (ss:SubState) resp = 
        FlResps.extractText resp 
        |> Option.map (fun text -> 
            let ss = ss.appendMsg (Assistant {id=resp.id; content=text})
            ss,[TFo_ChatUpdated ss.chat])
        |> Option.defaultValue (ss,[])

    ///if there is text content in resp then update substate chat and post updated chat message
    let emitTextAndPrompt (ss:SubState) resp = 
        let ss,_ = emitText ss resp
        let ss = ss.setPrompt true
        ss,[TFo_ChatUpdated ss.chat]

    let postCuaNext ss vs (cuaResp:Response) cuaInstr = 
        match vs, FlResps.computerCall cuaResp with 
        | Some (snapshot,w,h,url,env), Some cc ->            
            let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = env|}
            let cc_out = 
                {
                    call_id = cc.call_id
                    acknowledged_safety_checks = FlResps.safetyChecks cuaResp
                    output = Computer_creenshot {|image_url = snapshot |}
                    current_url = url
                }
                |> Computer_call_output
            let input = 
                match cuaInstr with 
                | Some text ->  
                    Log.info $"Reasoner guidance: `{text}`"
                    let textMsg = {Message.Default with content = [Content.Input_text {|text=text|}]}
                    [cc_out;InputOutputItem.Message textMsg]
                | None -> [cc_out]            
            let req = {Request.Default with
                            input = input; tools=[tool]
                            previous_response_id = Some cuaResp.id
                            store = true
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            FlResps.sendRequest W_Cua ss.bus.PostInput req
        | None,_ -> async {return failwith "no 'visual state' e.g. sceenshot width, height, given"}
        | _,None -> async {return failwith "no computer call output found in response"}
        |> FlResps.catch ss.bus.PostInput

    let ignoreMsg s msg name =
        Log.warn $"{name}: ignored message {msg}"
        F(s,[])

    ///convenience 'active pattern' to match a W_Reasoner msg
    ///with the given correlation id
    let (|Reasoner|_|) corrId msg = 
        match msg with 
        | W_Reasoner (id,resp) when id = corrId -> Some resp
        | _                                     -> None

    (* --- states --- *)

    let rec s_start ss msg = async {    
        match msg with 
        | W_Err e         -> return !!(s_terminate ss (Some e))
        | W_App TFi_Start -> let! (snapshot,w,h,url,env) = snapshot ss.driver
                             let ss = ss.appendSnapshot snapshot
                             FlResps.postStartCua ss.bus.PostInput ss.chat.systemMessage (snapshot,w,h,url,env)
                             return !!(s_loop ss)
        | x               -> Log.warn $"s_start: expecting {TFi_Start} message to start flow but got {x}"
                             return !!(s_start ss)
    }

    and s_loop ss msg = async {
        match msg with 
        | W_Err e                    -> return !!(s_terminate ss (Some e))
        | W_App TFi_EndAndReport     -> let corrId = postSummarizeProgress ss
                                        return !!(s_summarizing ss corrId) 
        | W_Cua resp when noCC resp  -> let ss,outMsgs = emitTextAndPrompt ss resp
                                        return F(s_pause ss, outMsgs)
        | W_Cua resp                 -> let ss,outMsgs1 = emitText ss resp
                                        let! ss,outMsgs2,visualState = performCall ss resp
                                        let corrId = postGetRsnrGuidanceForCua ss
                                        return F(s_reason ss (visualState,resp) corrId,outMsgs1 @ outMsgs2)
        | x                          -> return ignoreMsg (s_loop ss) x "s_loop"
    }            

    and s_reason ss (vs,cuaResp) corrId msg  = async {
        match msg with 
        | W_Err e                -> return !!(s_terminate ss (Some e))
        | W_App TFi_EndAndReport -> let corrId = postSummarizeProgress ss
                                    return !!(s_summarizing ss corrId) 
        | Reasoner corrId (resp) -> let cuaInstr = FlResps.extractText resp  
                                    postCuaNext ss vs cuaResp cuaInstr
                                    return !!(s_loop ss)
        | x                       -> return ignoreMsg (s_reason ss (vs,cuaResp) corrId) x "s_reason"
    }

    and s_pause ss msg = async {   
        match msg with 
        | W_Err e                -> return !!(s_terminate ss (Some e))
        | W_App TFi_EndAndReport -> let corrId = postSummarizeProgress ss
                                    return !!(s_summarizing ss corrId) 
        | W_App (TFi_Resume tx)  -> let ss = ss.appendMsg (User tx)
                                    let ss = ss.setPrompt false
                                    return F(s_loop ss,[TFo_ChatUpdated ss.chat])
        | x                      -> return ignoreMsg (s_pause ss) x "s_pause"
    }

    and s_summarizing ss corrId msg = async {
        match msg with 
        | W_Err e                 -> return !!(s_terminate ss (Some e))
        | Reasoner corrId (resp)  -> let ss = FlResps.extractText resp  
                                              |> Option.map (fun txt -> ss.appendMsg (Assistant {id=resp.id; content=txt})) 
                                              |> Option.defaultValue ss
                                     return F(s_terminate ss None, [TFo_Summary ss.chat])
        | x                       -> return ignoreMsg (s_summarizing ss corrId) x "s_summarizing"
    }

    and s_terminate ss (e:WErrorType option) msg = async {
        e 
        |> Option.iter (fun e -> 
            ss.bus.postOutput (TFo_Error e)
            Log.error (string e)
            ss.cts.CancelAfter(1000))
        Log.info $"s_terminate: message ignored {msg}"
        return !!(s_terminate ss None)
    }

    (* --- states [end] --- *)

    ///construct flow and also start it
    let create post driver chat : IFlow<TaskFLowMsgIn> =
        let bus = WBus.Create<TaskFLowMsgIn,TaskFLowMsgOut> post

        ///initial substate
        let ss0 = {
            cts=new CancellationTokenSource()
            chat = chat
            bus=bus
            driver=driver; snapshots=[]
            actions = []        
        }

        //initial state
        let s0 = s_start ss0

        //start flow
        Workflow.run ss0.cts.Token bus s0

        //return handler to talk to flow
        {new IFlow<TaskFLowMsgIn> with

            member _.Terminate () =                 
                async {
                    Log.info "terminating flow ..."
                    do! Async.Sleep(1000)
                    ss0.cts.Cancel()
                    ss0.bus.Close()
                }
                |> Async.Start
 
            member _.Post msg = bus.PostInput (W_App msg)
        }

