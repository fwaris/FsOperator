namespace FsOpBrowser
open WebViewControl
open Elmish
open FsOpCore

type Model = {
    url: string
    action : string
    webview : Ref<WebView option>

}

type Msg = 
    | SetUrl of string 
    | Initialize    


module Main = 
    open Avalonia.FuncUI.Hosts
    let init _   = 
        let model = {
            url = "https://www.amazon.com"
            webview = ref None
            action = ""
        }        
        model,Cmd.ofMsg Initialize    

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | SetUrl url -> {model with url = url}, Cmd.none
        with ex -> 
            Log.exn (ex,"error in browser message loop")
            model, Cmd.none
