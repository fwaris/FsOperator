namespace FsOperator
open System
open System.Threading.Channels
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts

module Update =
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

    let testSomething (model:Model) =
        async {
            try
                //do! Preview.drawArrow 100 500 50 0. 2000
                do! Preview.drawClick 100 500 2000
                //do! Preview.drawDragArrow (100,500) (300,600) 2000
                debug "Test something"
            with ex ->
                debug $"Error in testSomething: {ex.Message}"
        }
        |> Async.Start

    let init _   = 
        let url,instructions = StartPrompts.amazon
        //let url,instructions = StartPrompts.netflix
        //let url,instructions = StartPrompts.linkedIn
        //let url,instructions = StartPrompts.twitter
        
        let model = {
            instructions=instructions
            runState = None
            mailbox = Channel.CreateBounded(10)
            log = []
            output = ""
            initialized = false
            url = url
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
            | BrowserConnected -> {model with initialized=true},Cmd.none
            | Start ->
                match model.runState with 
                | None when model.initialized -> 
                    let runState = RunState.Create model.mailbox model.instructions
                    let runState = {runState with chatHistory = [Placeholder]}
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

            | Chat_Clear -> {model with runState = model.runState |> Option.map (fun rs -> {rs with chatHistory = []})}, Cmd.none
            | Chat_UpdateQuestion txt -> {model with runState = model.runState |> Option.map (fun rs -> {rs with chatHistory = Chat.updateQustion txt rs.chatHistory})}, Cmd.none
            | Chat_Append msg -> {model with runState = model.runState |> Option.map (fun rs -> {rs with chatHistory = Chat.append msg rs.chatHistory})}, Cmd.none
            | Chat_Respond -> {model with runState = model.runState |> Option.map (fun rs -> {rs with chatHistory = Chat.append (Question "") rs.chatHistory})}, Cmd.none

            | AppendLog txt -> {model with log = (txt:: model.log) |> List.truncate 100}, Cmd.none
            | ClearLog -> {model with log = []}, Cmd.none
            | SetUrl txt -> {model with url=txt}, Cmd.none

            | SetAction txt -> {model with action=txt}, Cmd.none
            | SetWarning txt -> {model with warning = txt},Cmd.none
            | TurnEnd -> {model with warning = "Current turn ended"}, Cmd.ofMsg Stop
            | StopWithError ex -> if model.runState.IsNone then model,Cmd.none else {model with warning = ex.Message}, Cmd.ofMsg Stop
            | TestSomething -> testSomething model; model, Cmd.none

            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex
            model, Cmd.none
