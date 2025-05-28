namespace FsOpCore
open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open System.Threading
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Channels
open FSharp.Control

type P2PFromClient =
    | Client_Connected of {| pid:int|}
    | Client_UrlSet of string
    | Client_Disconnect

type P2PFromServer =
    | Server_SetUrl of string
    | Server_Disconnect

module P2p =
    let defaultPort = 53091

    let serOptionsFSharp = 
        let o = JsonSerializerOptions(JsonSerializerDefaults.General)
        let opts = JsonFSharpOptions.Default()
        opts
            .WithSkippableOptionFields(true)            
            .AddToJsonSerializerOptions(o)        
        o
        
    let internal receiver<'t> (token:CancellationToken) (stream:NetworkStream) (poster:'t->unit) (disconnectionMsg:'t) =
        async {
            try 
                use strw = new StreamReader(stream,Encoding.UTF8)
                while not strw.EndOfStream do
                    let! line = strw.ReadLineAsync(token).AsTask() |> Async.AwaitTask
                    let msg = JsonSerializer.Deserialize<'t>(line,serOptionsFSharp)
                    Log.info $"P2P message received: {msg}"                    
                    poster msg
            with 
            | :? System.IO.IOException as ex ->  
                Log.info "P2P connection closed"
                poster disconnectionMsg //generate disconnection message internally
            | ex -> Log.exn (ex,"Error in p2p srever receive loop")                                           
        }            
        
    let internal sender<'t> (token:CancellationToken) (stream:NetworkStream) (channel:Channel<'t>) =
        async {
            use strw = new StreamWriter(stream,Encoding.UTF8)
            let mutable go = true
            while go && not token.IsCancellationRequested do 
                try 
                    let! (msg:'t)  = channel.Reader.ReadAsync(token).AsTask() |> Async.AwaitTask
                    let msgStr = JsonSerializer.Serialize(msg,serOptionsFSharp)
                    do! strw.WriteLineAsync(msgStr) |> Async.AwaitTask
                    do! strw.FlushAsync(token) |> Async.AwaitTask
                    Log.info $"P2P message sent: {msg}"                    
                with 
                | :? System.IO.IOException as ex ->  
                    Log.info "P2P connection closed"
                    go <- false
                | ex ->
                    do! Async.Sleep(1000)
                    Log.exn (ex,"Error in p2p server send loop")                                           
        }

    let internal messageLoop<'recvr,'sndr> token (stream:NetworkStream) poster outChannel disconnectionMsg  =         
        async {
            let! recvr = Async.StartChild (receiver<'recvr> token stream poster disconnectionMsg)
            let! sndr = Async.StartChild (sender<'sndr>  token stream outChannel)
            do! recvr
            do! sndr
        }
    
    let startServer port (token:CancellationToken) (poster:P2PFromClient->unit) (outChannel:Channel<P2PFromServer>)  =
        let ip = IPAddress.Loopback
        let listener = new TcpListener(ip, port)
        listener.Start()
        Log.info $"Server listening on {ip}:{port}"
        let comp = 
            async {
                use! client = listener.AcceptTcpClientAsync(token).AsTask() |> Async.AwaitTask
                use stream = client.GetStream()
                let loop = messageLoop token stream poster outChannel Client_Disconnect
                match! Async.Catch loop with
                | Choice1Of2 _ -> printfn "dispose p2p server"
                | Choice2Of2 ex -> Log.exn (ex,"Error in p2p server")
            }
        Async.Start(comp,token) 
        listener

    let startClient port (token:CancellationToken) (poster:P2PFromServer->unit) (outChannel:Channel<P2PFromClient>)  =
        let ip = IPAddress.Loopback
        let client = new TcpClient(ip.ToString(), port)
        Log.info $"Client connected on {ip}:{port}"        
        let comp = 
            async {
                use stream = client.GetStream()
                let loop = messageLoop token stream poster outChannel Server_Disconnect
                match! Async.Catch loop with
                | Choice1Of2 _ -> printfn "dispose p2p client"
                | Choice2Of2 ex -> Log.exn (ex,"Error in p2p client")
            }
        Async.Start(comp,token)
