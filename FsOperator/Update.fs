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
    open Avalonia.Threading
    open Avalonia.FuncUI.Hosts

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

    let initialInstructions = """
You should have a view of the my twitter feed.
Starting from that find all interesting new items related
to Generative AI and summarize them.
Don't go beyond 10 pages.
"""

    let init _   = 
        let model = {
            browser = None
            instructions=initialInstructions
            runState = None
            mailbox = Channel.CreateBounded(10)
            log = []
            output = ""
            url = Uri "https://twitter.com"
            webview = ref None
            action = ""
            warning = ""
        }        
        model,Cmd.ofMsg Initialize

    let token() = new System.Threading.CancellationTokenSource()

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | BrowserConnected pw -> {model with browser=Some pw},Cmd.none
            | Start ->
                match model.runState with 
                | None when model.browser.IsSome -> 
                    let runState = RunState.Create model.browser.Value model.mailbox model.instructions
                    ComputerUse.startMessaging runState
                    ComputerUse.sendStartMessage runState |> Async.Start
                    ComputerUse.loop runState 
                    {model with runState =  Some runState}, Cmd.none
                | _  -> debug "Already started or no browser set"; model, Cmd.none
            | Stop -> 
                match model.runState with
                | Some runState -> 
                    runState.tokenSource.Cancel()
                    runState.fromModel.Writer.TryComplete() |> ignore
                    runState.toModel.Writer.TryComplete()   |> ignore                    
                    {model with runState=None}, Cmd.none 
                | None -> 
                    model, Cmd.none
            | SetInstructions txt -> {model with instructions=txt}, Cmd.none
            | AppendLog txt -> 
                let log = txt :: model.log |> List.truncate 100
                {model with log=log}, Cmd.none
            | ClearLog -> {model with log = []}, Cmd.none
            | AppendOutput txt -> 
                let output = txt + Environment.NewLine + model.output
                let output = if output.Length > 10000 then output.Substring(0,10000) else output
                {model with output=output}, Cmd.none
            | ClearOutput -> {model with output = ""}, Cmd.none
            | SetUrl txt -> {model with url=Uri txt}, Cmd.none

            | SetAction txt -> {model with action=txt}, Cmd.none
            | SetWarning txt -> {model with warning = txt},Cmd.none
            | TurnEnd -> {model with warning = "Current turn ended"}, Cmd.ofMsg Stop
    
            | _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex
            model, Cmd.none
