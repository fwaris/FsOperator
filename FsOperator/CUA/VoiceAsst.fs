namespace FsOperator
open System.Text.Json
open FSharp.Control
open FsResponses
open RTOpenAI
open RTOpenAI.Api.Events
open System.Text.Json.Nodes
open FsOpCore
open FsResponses

module VoiceAsst =

    let sendInitResp conn = 
        (ClientEvent.ResponseCreate {ResponseCreateEvent.Default with
                                        event_id = Api.Utils.newId()
                                        response.instructions = Some "Announce that you will me accomplish tasks via the 'computer assistant'"
                                        //response.modalities = Some [M_AUDIO; M_TEXT]
                                        })
        |> Api.Connection.sendClientEvent conn

    //sends 'response.create' to prompt the LLM to generate audio (otherwise it seems to wait).
    let sendResponseCreate conn=
        (ClientEvent.ResponseCreate {ResponseCreateEvent.Default with
                                        event_id = Api.Utils.newId()
                                        //response.modalities = Some [M_AUDIO; M_TEXT]
                                        })
        |> Api.Connection.sendClientEvent conn
    
        
    let inline sendFunctionResponse conn (callId:string) result =
        let outEv =
            { ConversationItemCreateEvent.Default with
                item =
                      { ConversationItem.Default with
                          ``type`` = ConversationItemType.Function_call_output
                          call_id = Some callId
                          output = Some (JsonSerializer.Serialize(result))                                      
                      }
            }
            |> ConversationItemCreate
        Api.Connection.sendClientEvent conn outEv  //send prolog query results (or error)
        sendResponseCreate conn                    //prompt the LLM to respond now
        
    let getImageDesc (image:string) =
        async {
            try
                let img = Content.Input_image {|image_url=image|}
                let txt = Content.Input_text {|text = "describe the image"|}
                let msg = {Message.Default with content = [txt; img]} 
                let msgInput = IOitem.Message msg
                let req = {Request.Default with 
                                    input = [msgInput]
                                    store = false
                                    model=Models.gpt_41
                                    truncation = Some Truncation.auto
                              }
                let! resp = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                let txt = RUtils.outputText resp
                return txt
            with ex ->
                    Log.exn(ex,"getImageDesc")
                    return raise ex
        }
        
    let sendFunctionResponseWithImage conn (callId:string) (cuaResult:string) (image:string option) =
        async {
            match image with
            | Some image ->
                let! desc = getImageDesc image
                let resp = $"{cuaResult}\n### Screenshot description: {desc}"
                do sendFunctionResponse conn callId resp
            | None -> sendFunctionResponse conn callId cuaResult
        }
    
    let MAX_RETRY = 2

    let getArg (argName:string) (jsonStr:string) =    
        let jargs = JsonSerializer.Deserialize<JsonObject>(jsonStr)
        jargs.[argName].ToString()    

    let rec sendInstructions (taskState:TaskState) (ev:ResponseOutputItemDoneEvent) =
        async {
            try                                                 
                let instructions = ev.item.arguments |> Option.map (getArg "instructions") |> Option.defaultWith (fun _ -> failwith "function call argument not found")                
                Bus.postMessage taskState.bus (ClientMsg.VoiceChat_RunInstructions (instructions,ev.item.call_id))
                Bus.postLog taskState.bus $"<-- voice instr. {instructions}"
            with ex ->
                Bus.postWarning taskState.bus ex.Message
                Log.error $"Error in sendInstructions: {ex.Message}"
        }        

    let rec setGotoUrl (taskState:TaskState) (ev:ResponseOutputItemDoneEvent) =
        async {
            try                                                 
                let url = ev.item.arguments |> Option.map (getArg "url") |> Option.defaultWith (fun _ -> failwith "function call argument not found")                
                Bus.postMessage taskState.bus (ClientMsg.OpTask_SetTarget (url))
                Bus.postLog taskState.bus $"<-- goto url {url}"
                let conn = TaskState.voiceConnection (Some taskState)
                let conn = match conn.Value with Some c when c.WebRtcClient.State.IsConnected -> c | _ -> failwith "no connection to send responseCreate"
                sendFunctionResponse conn ev.item.call_id $"set url to {url}"
            with ex ->
                Bus.postWarning taskState.bus ex.Message
                Log.error $"Error in setGotoUrl: {ex.Message}"
        }    

module VoiceMachine =    
    let ASST_INSTRUCTIONS_FUNCTION = "assistantInstructions"
    let GOTO_URL_FUNCTION = "gotoUrl"
    let M_AUDIO = "audio"
    let M_TEXT = "text"
    let FUNCTION_CALL = "function_call"
    let FUNCTION_CALL_OUTPUT = "function_call_output"
    
    type State = {
        initialized : bool
        currentSession : Session  }      
    with static member Default = {
             initialized=false
             currentSession = Session.Default
             }
                            
    let ssInit = State.Default          //initial state for server event handling
        
    //take an existing session and 'update' it to new settings       
    let reconfigure instructions (s:Session) =
        { s with
            id = None                               //*** set 'id' and 'object' to None when updating an existing session
            object = None   
                                                    // set, unset, or override other fields as needed 
            instructions = instructions
            tool_choice = Some "auto"
            tools = [
                {
                    ``type`` = "function"
                    name = ASST_INSTRUCTIONS_FUNCTION
                    description = "Accepts a set of English instructions that be conveyed to an assistant to complete or continue a task"
                    parameters =
                        {
                            ``type`` = "object"
                            properties = Map.ofList ["instructions", {``type``= "string"; description= Some "detailed steps in English"}] 
                            required = ["instructions"]                            
                        }
                }
                {
                    ``type`` = "function"
                    name = GOTO_URL_FUNCTION
                    description = "Accepts a valid URL (e.g. https://www.microsoft.com), which will be used by the assistant to go to that web page"
                    parameters =
                        {
                            ``type`` = "object"
                            properties = Map.ofList ["url", {``type``= "string"; description= Some "valid web url"}] 
                            required = ["url"]                            
                        }
                }
            ]                
        }
        
    let toUpdateEvent (s:Session) =
        { SessionUpdateEvent.Default with
            event_id = Api.Utils.newId()
            session = s}
        |> SessionUpdate
            
    let sendUpdateSession instructions conn session =
        session
        |> reconfigure instructions
        |> toUpdateEvent
        |> Api.Connection.sendClientEvent conn
            
    let  isInstructionsCall (ev:ResponseOutputItemDoneEvent) =
        ev.item.``type`` = FUNCTION_CALL && ev.item.name = Some ASST_INSTRUCTIONS_FUNCTION
        
    let  getInstructions (ev:ResponseOutputItemDoneEvent) =
        if ev.item.``type`` = FUNCTION_CALL && ev.item.name = Some ASST_INSTRUCTIONS_FUNCTION then  
            ev.item.arguments
        else
            Some "no instructions found"

    let  isGotoUrlCall (ev:ResponseOutputItemDoneEvent) =
        ev.item.``type`` = FUNCTION_CALL && ev.item.name = Some GOTO_URL_FUNCTION
        
    let  getUrl (ev:ResponseOutputItemDoneEvent) =
        if ev.item.``type`` = FUNCTION_CALL && ev.item.name = Some GOTO_URL_FUNCTION then  
            ev.item.arguments
        else
            Some "no instructions found"
                   
    let  isFunctionCallResult (ev:ResponseOutputItemDoneEvent) =
        ev.item.``type`` = FUNCTION_CALL_OUTPUT && 
            (ev.item.name = Some ASST_INSTRUCTIONS_FUNCTION
            || ev.item.name = Some GOTO_URL_FUNCTION)
                       
    // accepts old state and next event - returns new state
    let update (taskState:TaskState) conn (st:State) ev =
        async {
            match ev with
            | SessionCreated s when not st.initialized ->  sendUpdateSession (Some(TaskState.voiceAsstInstructions (Some taskState))) conn s.session; return {st with initialized = true} 
            | SessionCreated s -> return {st with currentSession = s.session }
            | SessionUpdated s -> VoiceAsst.sendInitResp conn; return {st with currentSession = s.session }
            | ResponseOutputItemDone ev when isInstructionsCall ev  -> 
                Log.info $"<-- function call {ev.item.name}"
                if (TaskState.functionId ASST_INSTRUCTIONS_FUNCTION taskState).IsSome then 
                    Log.info $"Ignoring function call {ev.item.name} as we are already processing a {ASST_INSTRUCTIONS_FUNCTION} function call"
                else
                    VoiceAsst.sendInstructions taskState ev |> Async.Start
                return st
            | ResponseOutputItemDone ev when isGotoUrlCall ev  -> 
                Log.info $"<-- function call {ev.item.name}"
                if (TaskState.functionId GOTO_URL_FUNCTION taskState).IsSome then 
                    Log.info $"Ignoring function call {ev.item.name} as we are already processing {GOTO_URL_FUNCTION} function call"
                else                    
                    VoiceAsst.setGotoUrl taskState ev |> Async.Start
                return st
            | ResponseOutputItemDone ev when isFunctionCallResult ev  -> return  st            
            | ResponseTextDelta ev -> Bus.postLog taskState.bus $"text delta {ev}"; return st
            | ResponseAudioDelta _
            | ResponseAudioTranscriptDelta _
            | ResponseFunctionCallArgumentsDelta _ -> return st // suppress logging 'delta' events 
            | other -> (* Log.info $"unhandled event: {other}"; *) return st //log other events
        }
        
    let private startReader (taskState:TaskState) (conn:RTOpenAI.Api.Connection) = 
        let task =
            async {
                let comp = 
                    conn.WebRtcClient.OutputChannel.Reader.ReadAllAsync(taskState.tokenSource.Token)
                        |> AsyncSeq.ofAsyncEnum
                        |> AsyncSeq.map Api.Exts.toEvent
                        |> AsyncSeq.scanAsync (update taskState conn) ssInit   //handle actual event
                        |> AsyncSeq.iter (fun s -> ())
                match! Async.Catch comp with
                | Choice1Of2 _ -> RTOpenAI.Api.Log.info "server events completed"
                | Choice2Of2 exn -> RTOpenAI.Api. Log.exn(exn,"Error: Machine.run")
            }
        Async.Start(task, taskState.tokenSource.Token)

    let stopVoiceMachine (connection:Ref<Api.Connection option>) =
        match connection.Value with 
        | Some conn -> 
            RTOpenAI.Api.Connection.close conn
            connection.Value <- None
        | None -> ()
            
    let startVoiceMachine (taskState:TaskState) =
        async {
            let connection = TaskState.voiceConnection (Some taskState)            
            stopVoiceMachine connection
            let conn = RTOpenAI.Api.Connection.create()
            let keyReq = {Api.Exts.KeyReq.Default with model = C.OPENAI_RT_MODEL_GPT4O}
            let! ephemeralKey = Api.Exts.getOpenAIEphemKey (getApiKey()) keyReq |> Async.AwaitTask
            do! RTOpenAI.Api.Connection.connect ephemeralKey conn |> Async.AwaitTask
            startReader taskState conn
            connection.Value <- Some conn
        }
