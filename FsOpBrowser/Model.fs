namespace FsOpBrowser
open WebViewControl
open System
open Elmish
open FSharp.Control
open FsOpCore
open System.Threading.Channels

type Msg = 
    | SetUrl of string 
    | AckUrl of string
    | Close of string 
    | Initialize    
    | ConnectedToServer of unit
    | Error of exn

type Model = {
    url: string
    action : string
    webview : Ref<WebView option>
    mailbox : Channel<Msg>
    outChannel : Channel<P2PFromClient>
    tokenSource : System.Threading.CancellationTokenSource
}

module Main = 

    let mutable clientId = ""
    let mutable port = P2p.defaultPort

    open Avalonia.FuncUI.Hosts
    let init _   = 
        let model = {
            url = "https://www.amazon.com"
            webview = ref None
            action = ""
            mailbox = Channel.CreateBounded(10)
            outChannel = Channel.CreateBounded(10)
            tokenSource = new System.Threading.CancellationTokenSource()
        }        
        model,Cmd.ofMsg Initialize    

    let subscribeBackground (model:Model) =
        let backgroundEvent dispatch =
            let ctx = new System.Threading.CancellationTokenSource()
            let comp =
                async{
                    let comp =
                         model.mailbox.Reader.ReadAllAsync()
                         |> AsyncSeq.ofAsyncEnum
                         |> AsyncSeq.iter dispatch
                    match! Async.Catch(comp) with
                    | Choice1Of2 _ -> printfn "dispose subscribeBackground"
                    | Choice2Of2 ex -> printfn "%s" ex.Message
                }
            Async.Start(comp,ctx.Token)
            {new IDisposable with member _.Dispose() = ctx.Dispose(); printfn "disposing subscription backgroundEvent";}
        backgroundEvent

    let subscriptions model =

        let sub2 = subscribeBackground model
        [
            [nameof sub2], sub2
        ]

    let msgMap = function
        | P2PFromServer.Server_CloseClient incomingId ->  Close incomingId
        | P2PFromServer.Server_SetUrl url -> SetUrl url

    let connect model =
        async {
            let poster = msgMap>>model.mailbox.Writer.TryWrite>>ignore
            P2p.startClient port model.tokenSource.Token poster model.outChannel
            ()
        }

    let postToServer model msg =
        model.outChannel.Writer.TryWrite msg |> ignore

    let postConnectAck model =         
        let msg = P2PFromClient.Client_Connected {| clientId=clientId; pid=System.Diagnostics.Process.GetCurrentProcess().Id|}
        postToServer model msg

    let postUrlSet model url =
        let msg = P2PFromClient.Client_UrlSet url
        postToServer model msg
        
    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.OfAsync.either connect model ConnectedToServer Error
            | ConnectedToServer _ -> postConnectAck model; model, Cmd.none
            | SetUrl url -> {model with url = url},Cmd.ofMsg (AckUrl url)
            | Close id -> (if clientId = id then Environment.Exit(0)); model, Cmd.none
            | AckUrl url -> postUrlSet model url; model, Cmd.none
            | Error exn -> Log.exn(exn,""); model, Cmd.none //terminate app
        with ex -> 
            Log.exn (ex,"error in browser message loop")
            model, Cmd.none
