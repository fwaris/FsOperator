namespace FsOperator
open System
open System.Threading.Channels
open Elmish
open Avalonia.Media
open Avalonia
open Avalonia.Controls
open FSharp.Control
open Avalonia.FuncUI.Hosts

module Update =
    let initialInstructions = """
You should have a view of the my linkedin home page.
Starting from that find all interesting new items related
to Generative AI and summarize them.
Don't go beyone 5 pages.
"""

    let init _   = 
        let model = {
            playwright=None
            instructions=initialInstructions
            toModel = Channel.CreateBounded(10)
            fromModel = Channel.CreateBounded(10)
            tokenSource = None
            log = ""
            output = ""
            url = Uri "https://linkedin.com"
            webview = ref None
        }        
        model,Cmd.ofMsg Initialize

    let token() = new System.Threading.CancellationTokenSource()

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | BrowserConnected pw -> {model with playwright=Some pw},Cmd.ofMsg Start
            | Start ->
                match model.tokenSource with 
                | None -> 
                    let tkn = token()
                    ComputerUse.start tkn model.fromModel model.toModel
                    {model with tokenSource= Some tkn}, Cmd.none
                | Some tkn -> model, Cmd.none
            | Stop -> 
                match model.tokenSource with
                | Some tkn -> 
                    tkn.Cancel()
                    {model with tokenSource=None}, Cmd.none
                | None -> model, Cmd.none
            | SetInstructions txt -> {model with instructions=txt}, Cmd.none
            | AppendLog txt -> 
                let log = txt + Environment.NewLine + model.log
                let log = if log.Length > 10000 then log.Substring(0,10000) else log
                {model with log=log}, Cmd.none
            | ClearLog -> {model with log = ""}, Cmd.none
            | AppendOutput txt -> 
                let output = txt + Environment.NewLine + model.output
                let output = if output.Length > 10000 then output.Substring(0,10000) else output
                {model with output=output}, Cmd.none
            | ClearOutput -> {model with output = ""}, Cmd.none
            | SetUrl txt -> {model with url=Uri txt}, Cmd.none
    
            | _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex
            model, Cmd.none
