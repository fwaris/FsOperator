module Pgm
open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open RTOpenAI.WebRTC
open System.Text.Json
open Microsoft.Extensions.Logging

module C = 
    let OPENAI_RT_API = "https://api.openai.com/v1/realtime"
    let OPENAI_SESSION_API = "https://api.openai.com/v1/realtime/sessions"
    let OPENAI_RT_MODEL_GPT4O = "gpt-4o-realtime-preview"
    let OPENAI_RT_MODEL_GPT4O_MINI = "gpt-4o-mini-realtime-preview"
    let OPENAI_RT_DATA_CHANNEL = "oai-events"   

module ConfigEnv = 
    let scKey = [|210uy; 80uy; 59uy; 99uy; 113uy; 133uy; 1uy; 210uy; 5uy; 158uy; 216uy; 189uy;
  147uy; 50uy; 73uy; 11uy|]


    let setEnv() = 
        let encKey = ""

        encKey
        |> SimpleCrypt.decr scKey
        |> System.Convert.FromBase64String
        |> System.Text.Encoding.UTF8.GetString
        |> fun k -> System.Environment.SetEnvironmentVariable("OPENAI_API_KEY",k)

    
type KeyReq = {
    model : string
    modalities : string list
    instructions : string
}
with static member Default = {
        model = ""
        modalities = ["audio"; "text"]
        instructions = "You are a friendly assistant"
    }        

let getOpenAIEphemKey apiKey (modelId:string) =
    task {
        let input = {KeyReq.Default with model = modelId}
        let! resp = RTOpenAI.Api.Exts.callApi<_,RTOpenAI.Api.Events.Session>(apiKey,RTOpenAI.Api.C.OPENAI_SESSION_API,input)
        return
            resp.client_secret
            |> Option.map _.value
            |> Option.defaultWith (fun _ -> failwith "Unable to get ephemeral key")
    }        

let rec run (ctx:CancellationTokenSource) (client:IWebRtcClient) = 
    async {
        while not ctx.IsCancellationRequested  do
            try 
                let! result = client.OutputChannel.Reader.ReadAsync(ctx.Token).AsTask() |> Async.AwaitTask
                let msg = RTOpenAI.Api.Exts.toEvent result
                //printfn "%A" msg
                printfn "."
            with ex -> 
                printfn "Error: %s" ex.Message
    }

let mutable disps = []

let subsribState (client:IWebRtcClient) = 
    let disp = client.StateChanged.Subscribe(fun state -> 
        printfn "************************"
        match state with
        | State.Connected -> printfn "Connected"
        | State.Disconnected -> printfn "Disconnected"
        | State.Connecting -> printfn "Connecting"
        printfn "************************"

    )
    disps <- disp :: disps

let connect() = 
    async {
        let getKey() = Environment.GetEnvironmentVariable("OPENAI_API_KEY")

        let key = getKey()
        if String.IsNullOrEmpty(key) then
            failwith "API key is not set. Please set the OPENAPI_API_KEY environment variable."
        let! ephemeralKey = getOpenAIEphemKey key C.OPENAI_RT_MODEL_GPT4O |> Async.AwaitTask
        let client = WebRtc.create()       
        subsribState client
        let! disp = client.Connect(ephemeralKey, C.OPENAI_RT_API, None) |> Async.AwaitTask        
        return client
    }
    

[<EntryPoint>]
let main argv =
    ConfigEnv.setEnv()
    printfn "Starting OpenAI Realtime Client... enter to continue"
    Console.ReadLine() |> ignore
    let client = connect() |> Async.RunSynchronously
    
    let ctx = new CancellationTokenSource()
    
    run ctx client |> Async.Start

    Console.ReadLine() |> ignore

    ctx.Cancel()
    disps |> List.iter (fun d -> d.Dispose())
    client.Dispose()
    0 
