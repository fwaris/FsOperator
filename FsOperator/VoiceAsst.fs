namespace FsOperator
open System.Text.Json
open FSharp.Control
open RTOpenAI
open RTOpenAI.Api.Events
open System.Text.Json.Nodes
open FsOpCore

module VoicePrompts =
    let rtInstructions = $"""
You are to collaborate with a user to help complete a task.
The task is performed by a separate 'assistant'. 
You job is to converse with the human user to generate and sumbit the instructions to the assistant.
The assistant will carry out the task and return with a response - which may be a question or a clarification.
Again, converse with the user before generating the next instruction for the assistant.
Always confirm with the user first before sending the instructions to the assistant.

To send the instructions to the assitant use function call 'assistantInstructions' with the instructions as the argument.

Note the assistant may take a while to get back to you so be patient.

You can use the same 'assistantantInstructions' function to respond to the results of the previous assistant's reponse.

"""

module VoiceAsst =

    let sendInitResp conn = 
        (ClientEvent.ResponseCreate {ResponseCreateEvent.Default with
                                        event_id = Api.Utils.newId()
                                        response.instructions = Some "briefly introduce yourself"
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
    
    let MAX_RETRY = 2

    let getArg (argName:string) (jsonStr:string) =    
        let jargs = JsonSerializer.Deserialize<JsonObject>(jsonStr)
        jargs.[argName].ToString()    

    let rec sendInstructions (runState:RunState) (ev:ResponseOutputItemDoneEvent) =
        async {
            try                                                 
                let instructions = ev.item.arguments |> Option.map (getArg "instructions") |> Option.defaultWith (fun _ -> failwith "function call argument not found")                
                Bus.postMessage runState.bus (ClientMsg.VoiceChat_RunInstructions (instructions,ev.item.call_id))
                Bus.postLog runState.bus $"<-- voice instr. {instructions}"
            with ex ->
                Bus.postWarning runState.bus ex.Message
                RTOpenAI.Api.Log.error $"Error in runInstructions: {ex.Message}"
        }        

module VoiceMachine =    
    let ASST_INSTRUCTIONS_FUNCTION = "assistantInstructions"
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
                            required = []                            
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
      
        //if ev.item.``type`` = FUNCTION_CALL && ev.item.name = Some ASST_INSTRUCTIONS_FUNCTION then
        //     ev.item.arguments |> Option.defaultValue "no instructions found"
        //else
        //    "no instructions: incorrect response type"
             
    let  isInstructionsResult (ev:ResponseOutputItemDoneEvent) =
        ev.item.``type`` = FUNCTION_CALL_OUTPUT && ev.item.name = Some ASST_INSTRUCTIONS_FUNCTION
                
       
    // accepts old state and next event - returns new state
    let update (runState:RunState) conn (st:State) ev =
        async {
            match ev with
            | SessionCreated s when not st.initialized ->  sendUpdateSession (Some(RunState.voiceAsstInstructions (Some runState))) conn s.session; return {st with initialized = true} 
            | SessionCreated s -> return {st with currentSession = s.session }
            | SessionUpdated s -> VoiceAsst.sendInitResp conn; return {st with currentSession = s.session }
            | ResponseOutputItemDone ev when isInstructionsCall ev  -> 
                Log.info $"<-- function call {ev.item.name}"
                if runState.lastFunctionCallId.Value.IsSome then 
                    debug $"Ignoring function call {ev.item.name} as we are already processing a function call"
                else
                    VoiceAsst.sendInstructions runState ev |> Async.Start
                return st
            | ResponseOutputItemDone ev when isInstructionsResult ev  -> return  st            
            | ResponseTextDelta ev -> Bus.postLog runState.bus $"text delta {ev}"; return st
            | ResponseAudioDelta _
            | ResponseAudioTranscriptDelta _
            | ResponseFunctionCallArgumentsDelta _ -> return st // suppress logging 'delta' events 
            | other -> (* Log.info $"unhandled event: {other}"; *) return st //log other events
        }
        

    let private startReader (runState:RunState) (conn:RTOpenAI.Api.Connection) = 
        let task =
            async {
                let comp = 
                    conn.WebRtcClient.OutputChannel.Reader.ReadAllAsync(runState.tokenSource.Token)
                        |> AsyncSeq.ofAsyncEnum
                        |> AsyncSeq.map Api.Exts.toEvent
                        |> AsyncSeq.scanAsync (update runState conn) ssInit   //handle actual event
                        |> AsyncSeq.iter (fun s -> ())
                match! Async.Catch comp with
                | Choice1Of2 _ -> RTOpenAI.Api.Log.info "server events completed"
                | Choice2Of2 exn -> RTOpenAI.Api. Log.exn(exn,"Error: Machine.run")
            }
        Async.Start(task, runState.tokenSource.Token)

        
    let startVoiceMachine (runState:RunState) =
        async {
            match runState.chatMode with
            | CM_Voice v when v.connection.Value.IsSome && v.connection.Value.Value.WebRtcClient.State.IsConnected  -> return()
            | CM_Voice v ->
                match v.connection.Value with
                | Some x -> RTOpenAI.Api.Connection.close x
                | None -> ()
                let conn = RTOpenAI.Api.Connection.create()
                let keyReq = {Api.Exts.KeyReq.Default with model = C.OPENAI_RT_MODEL_GPT4O}
                let! ephemeralKey = Api.Exts.getOpenAIEphemKey (getApiKey()) keyReq |> Async.AwaitTask
                do! RTOpenAI.Api.Connection.connect ephemeralKey conn |> Async.AwaitTask
                startReader runState conn
                v.connection .Value <- Some conn
                return ()
            | x -> Bus.postLog runState.bus $"Chat mode not supported {x}"
            return ()
        }

    let stopVoiceMachine (runState:RunState) =
        async {
            match runState.chatMode with
            | CM_Voice v when v.connection.Value.IsSome -> 
                RTOpenAI.Api.Connection.close v.connection.Value.Value
                v.connection.Value <- None
                return ()
            | CM_Voice _ -> Abort(None,"Voice connection not set") |> Bus.postMessage runState.bus
            | x -> Bus.postLog runState.bus $"Chat mode not supported {x}"
            return ()
        }
