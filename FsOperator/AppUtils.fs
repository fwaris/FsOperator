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

