namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.FuncUI.DSL
open WebViewControl.Ext
()
(*
open Avalonia.FuncUI.Hosts
open Avalonia.Remote.Protocol

module RemoteMain = 
    type RModel = {
        remoteTransport : IAvaloniaRemoteTransportConnection option
    }

    //let view model dispatch =
    //    match model.remoteTransport with 
    //    | Some t -> 
    //        RemoteWidget.create(t)  [
                
    //            Grid.create [
    //                Grid.columnDefinitions "1*"
    //                Grid.rowDefinitions "1*"
    //                Grid.children [
    //                    WebViewControl.WebView.create [
    //                        WebViewControl.WebView.address "https://www.google.com"
    //                        WebViewControl.WebView.horizontalAlignment HorizontalAlignment.Stretch
    //                        WebViewControl.WebView.verticalAlignment VerticalAlignment.Stretch
    //                    ]
             
    //                 ]
    //            ]
    //        ]
    //    | None -> 
    //        TextBlock.create [
    //            TextBlock.text "Loading ..."
    //            TextBlock.horizontalAlignment HorizontalAlignment.Center
    //            TextBlock.verticalAlignment VerticalAlignment.Center
    //        ]



type RemoteHostWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "Browser"
        base.Width <- 800.0
        base.Height <- 400.0

        //Program.mkProgram Update.init (Update.update this) MainView.main
        //|> Program.withHost this
        //|> Program.withSubscription Update.subscriptions        
        ////|> Program.withConsoleTrace        
        //|> Program.runWithAvaloniaSyncDispatch ()



module RemoteBrowser =
    open Avalonia.Remote.Protocol
    open Avalonia.Remote.Protocol.Viewport
    let mutable private _connection : IAvaloniaRemoteTransportConnection option = None
    let mutable private _messageHander = Action<IAvaloniaRemoteTransportConnection,obj>(fun a b -> ())

    let disconnect() =
        match _connection with 
        | Some c -> 
            c.remove_OnMessage _messageHander
            c.Dispose(); 
            _connection <- None
        | None -> ()

    let connect (newConnection:IAvaloniaRemoteTransportConnection) = 
        disconnect()
        newConnection.add_OnMessage (_messageHander)
        _connection <- Some newConnection       

    let start() = 
        let btx = BsonTcpTransport()
        btx.Listen(Net.IPAddress.Loopback,C.REMOTE_BROWSER_PORT, connect)

    let str() = 
       let rc = Avalonia.Controls.Remote.RemoteWidget(_connection.Value)
       
       ()

*)