namespace FsOpCore
open System
open RTOpenAI.Api
open RTOpenAI.Api.Events
open System.Text.Json
open Microsoft.SemanticKernel
open FSharp.Control

module Voice =

    ///Convert voice usage type to FsResponses usage type
    let toResponsesUsage (u:RTOpenAI.Api.Events.Usage) =
        {
            FsResponses.input_tokens = u.input_tokens
            FsResponses.output_tokens = u.output_tokens
            FsResponses.total_tokens = u.total_tokens
        }

    ///Convert FsRespones.Function to RTOpenAI.Api.Events.FunctionTool
    let toVoiceTool (respTool:FsResponses.Function) : RTOpenAI.Api.Events.FunctionTool =
        {
            ``type`` = "function"
            name = respTool.name
            description = respTool.description
            parameters =
                {
                    ``type`` = respTool.parameters.``type``
                    properties = respTool.parameters.properties |> Map.map (fun k v -> {description = Some v.description; ``type``=v.``type``})
                    required = respTool.parameters.required
                }

        }

    ///Matches function call request from Voice Assistant
    let (|FuncCall|_|) msg =
        match msg with
        | W_Voice ( ResponseOutputItemDone ev) -> if ev.item.``type`` = C.FUNCTION_CALL && ev.item.name.IsSome && ev.item.arguments.IsSome then
                                                        Some(ev.item.call_id, ev.item.name.Value,ev.item.arguments.Value)
                                                    else
                                                        None
        | _                                    -> None

    let reconfigure tools instructions (s:Session) =
        { s with
            id = None                               //*** set 'id' and 'object' to None when updating an existing session
            object = None
                                                    // set, unset, or override other fields as needed
            instructions = instructions
            tool_choice = Some "auto"
            tools = tools
        }

    let toUpdateEvent (s:Session) =
        { SessionUpdateEvent.Default with
            event_id = Utils.newId()
            session = s}
        |> SessionUpdate

    let voiceTools = lazy(
        let tools = FlUtils.makeFunctionTools<Functions.FsOpVoice>() |> List.map FsResponses.Tool.Function
        tools |> List.choose (function FsResponses.Tool.Function f -> toVoiceTool f |> Some | _ -> None))

    let sendUpdateSession instructions conn session =
        session
        |> reconfigure voiceTools.Value instructions
        |> toUpdateEvent
        |> Connection.sendClientEvent conn

    let sendResponseCreate conn=
        (ClientEvent.ResponseCreate {ResponseCreateEvent.Default with
                                        event_id = Utils.newId()
                                        //response.modalities = Some [M_AUDIO; M_TEXT]
                                        })
        |> Connection.sendClientEvent conn


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
        RTOpenAI.Api.Connection.sendClientEvent conn outEv
        sendResponseCreate conn    //ask voice asst to respond

    let callFunction (conn:Connection) (kernel:Kernel) (callId,name,arguments) =
        async {
            let! rslt = FlUtils.invokeFunction kernel name arguments
            sendFunctionResponse conn callId rslt
        }


    ///Pump events from voice asst. into task bus
    let rec startMessagePump (conn:Connection) (task:TaskState<_,_>) =
        let comp =
            conn.WebRtcClient.OutputChannel.Reader.ReadAllAsync()
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iter(fun m -> task.bus.PostInput(W_Voice (Exts.toEvent m)))
        async{
            match! Async.Catch comp with
            | Choice1Of2 _ -> Log.info "Voice connection endded"
            | Choice2Of2 ex ->
                Log.exn(ex,nameof startMessagePump)
                task.bus.PostInput (W_Err (WE_Exn ex))
        }
        |> Async.Start

    let startVoice (conn:Connection) (task:TaskState<_,_>) = async {
        startMessagePump conn task
        let keyReq = Exts.KeyReq.Default
        let key = Environment.GetEnvironmentVariable(FsResponses.RUtils.API_KEY_ENV_VAR)
        let! ephemKey = RTOpenAI.Api.Exts.getOpenAIEphemKey key keyReq |> Async.AwaitTask
        do! Connection.connect ephemKey conn |> Async.AwaitTask
    }
