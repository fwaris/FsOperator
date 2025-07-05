namespace FsOpCore
open System
open RTOpenAI.Api
open RTOpenAI.Api.Events
open System.Text.Json
open Microsoft.SemanticKernel
open FSharp.Control

module Voice = 
    ///Matches function call request from Voice Assistant
    let (|FuncCall|_|) msg = 
        match msg with 
        | W_Voice ( ResponseOutputItemDone ev) -> if ev.item.``type`` = C.FUNCTION_CALL && ev.item.name.IsSome && ev.item.arguments.IsSome then 
                                                        Some(ev.item.call_id, ev.item.name.Value,ev.item.arguments.Value)
                                                    else 
                                                        None
        | _                                    -> None


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
