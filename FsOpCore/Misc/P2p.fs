namespace FsOpCore
open System
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open System.Threading
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Channels

type P2PFromClient =
    | Client_Connected of {|clientId:string; pid:int|}
    | Client_UrlSet of string
    | Client_Closed of string

type P2PFromServer =
    | Server_CloseClient of string
    | Server_SetUrl of string

module P2p =
    let defaultPort = 53091

    let serOptionsFSharp = 
        let o = JsonSerializerOptions(JsonSerializerDefaults.General)
        let opts = JsonFSharpOptions.Default()
        opts
            .WithSkippableOptionFields(true)            
            .AddToJsonSerializerOptions(o)        
        o
        
    let internal receiver<'t> (token:CancellationToken) (stream:NetworkStream) (poster:'t->unit) =
        task {
            while not token.IsCancellationRequested do 
                try 
                    let! (msg:'t) = JsonSerializer.DeserializeAsync<'t>(stream,serOptionsFSharp,token)
                    poster msg
                with ex ->
                    Log.exn (ex,"Error in p2p srever receive loop")                                           
        }            
        
    let internal sender<'t> (token:CancellationToken) (stream:NetworkStream) (channel:Channel<'t>)  =
        task {
            while not token.IsCancellationRequested do 
                try 
                    let! (msg:'t)  = channel.Reader.ReadAsync(token)
                    do! JsonSerializer.SerializeAsync(stream,msg,serOptionsFSharp)
                with ex ->
                    Log.exn (ex,"Error in p2p server send loop")                                           
        }

    let internal messageLoop token stream poster outChannel  =         
        async {
            let! recvr = Async.StartChild (Async.AwaitTask (receiver token stream poster))
            let! sndr = Async.StartChild (Async.AwaitTask (sender  token stream outChannel))
            do! recvr
            do! sndr
        }
    
    let startServer clientId port (token:CancellationToken) (poster:P2PFromClient->unit) (outChannel:Channel<P2PFromServer>)  =
        let ip = IPAddress.Loopback
        let listener = new TcpListener(ip, port)

        listener.Start()
        printfn "Server listening on %O:%d" ip port
        let comp = 
            async {
                use! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
                printfn "Client connected"
                use stream = client.GetStream()
                let loop = messageLoop token stream poster outChannel
                match! Async.Catch loop with
                | Choice1Of2 _ -> printfn "dispose p2p server"
                | Choice2Of2 ex -> Log.exn (ex,"Error in p2p server")
            }
        Async.Start(comp,token) 

    let startClient port (token:CancellationToken) (poster:P2PFromServer->unit) (outChannel:Channel<P2PFromClient>)  =
        let ip = IPAddress.Loopback
        let client = new TcpClient(ip.ToString(), port)
        printfn "client connected %O:%d" ip port
        let comp = 
            async {
                use stream = client.GetStream()
                let loop = messageLoop token stream poster outChannel
                match! Async.Catch loop with
                | Choice1Of2 _ -> printfn "dispose p2p client"
                | Choice2Of2 ex -> Log.exn (ex,"Error in p2p client")
            }
        Async.Start(comp,token)
