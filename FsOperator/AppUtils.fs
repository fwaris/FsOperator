namespace FsOperator

module AppUtils =

    let getOpenAIEphemKey apiKey =
        async {
            let input = {KeyReq.Default with model = C.OPENAI_RT_MODEL_GPT4O}
            let! resp = RTOpenAI.Api.Exts.callApi<_,RTOpenAI.Api.Events.Session>(apiKey,RTOpenAI.Api.C.OPENAI_SESSION_API,input) |> Async.AwaitTask
            return
                resp.client_secret
                |> Option.map _.value
                |> Option.defaultWith (fun _ -> failwith "Unable to get ephemeral key")
        }        

    let postMessage (runState:RunState) msg = runState.mailbox.Writer.TryWrite(msg) |> ignore
    let postLog (runState:RunState) msg =  postMessage runState (ClientMsg.AppendLog msg) 
    let postAction (runState:RunState) action = postMessage runState (SetAction action) 
    let postWarning (runState:RunState) warning = postMessage runState (StatusMsg_Set warning)




