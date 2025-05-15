namespace FsOperator
open System
open System.Linq.Expressions
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts
open FsOpCore

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
        model,Cmd.none

    let testSomething_ (model:Model) =
        let testChat =
            [
                Assistant {id = "1"; content = Instructions.getTextChat model.instructions}
                User "The quick brown fox jumped over the lazy dog"
                Assistant {id = "2"; content = "How can I help you?"}
            ]
        let runState = RunState.Create model.mailbox model.instructions
        let runState = {runState with chatMode = CM_Text {Chat.Default with messages=testChat} ; cuaState = CUAState.CUA_Pause}
        {model with runState =  Some runState}, Cmd.none


    let init _   =
        let url,instructions = StartPrompts.amazon
        //let url,instructions = StartPrompts.netflix
        //let url,instructions = StartPrompts.linkedIn
        //let url,instructions = StartPrompts.twitter
        //sResponses.debug_logging <- true
        let model = {
            instructions = Instructions.sample
            runState = None
            mailbox = Channel.CreateBounded(10)
            log = []
            url = url
            action = ""
            statusMsg = None,""
            browserState = BrowserAppState.Create()
            isFlashing = false
        }        
        model,Cmd.ofMsg Initialize
        //model,Cmd.ofMsg InitializeDevMode

    let shouldClearStatus (inComingDT:DateTime option) messageDT =
        match inComingDT,messageDT with
        | None, None -> true
        | Some inComingDT, None -> true
        | Some inComingDT, Some messageDT -> messageDT = inComingDT
        | None, Some messageDT -> false

    let delayClearStatus (time:DateTime) =
        async {
            do! Async.Sleep 10000
            return Some time
        }

    let delayFlash isOn =
            async {
                do! Async.Sleep 1000
                return isOn
            }

    ///start or stop text mode task
    let startStopForTextChat model =
        match model.runState with 
        | None when model.browserState.state.IsBST_Ready -> 
            let runState = {RunState.initForText model.mailbox model.instructions with cuaState = CUA_Loop}
            ComputerUse.startMessaging (runState.tokenSource.Token,runState.bus)
            ComputerUse.sendStartMessage runState.bus (Instructions.getTextChat model.instructions) |> Async.Start
            ComputerUse.loop runState 
            {model with runState =  Some runState}, Cmd.none
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Text -> 
            runState.tokenSource.Cancel()
            runState.bus.fromCua.Writer.TryComplete() |> ignore
            runState.bus.toCua.Writer.TryComplete()   |> ignore                    
            {model with runState=Some {runState with cuaState = CUA_Init}}, Cmd.none 
        | _ -> model, Cmd.none //ignore any other state

    ///start or stop voice mode task
    let startStopForVoiceChat (model:Model) = 
        match model.runState with 
        | None when model.browserState.state.IsBST_Ready -> 
            let runState = {RunState.initForVoice model.mailbox model.instructions with cuaState = CUA_Loop}
            ComputerUse.startMessaging (runState.tokenSource.Token, runState.bus)
            ComputerUse.loop runState 
            VoiceMachine.startVoiceMachine runState |> Async.Start
            {model with runState =  Some runState}, Cmd.none
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Voice -> 
            runState.tokenSource.Cancel()
            runState.bus.fromCua.Writer.TryComplete() |> ignore
            runState.bus.toCua.Writer.TryComplete()   |> ignore                    
            VoiceMachine.stopVoiceMachine runState |> Async.Start
            {model with runState = Some {runState with cuaState = CUA_Init}}, Cmd.none
        | _ -> model, Cmd.none //ignore voice mode

    ///cua assistant loop has stopped and we need to respond to the assistant's last message (and chatHistory)
    ///For voice mode we send the cua assistant's last message to the voice assistant
    ///For text, no action required (for now until reasoning is enabled) the user can see the message and respond 
    let handleTurnEnd model =
        let model = {model with runState = RunState.setState CUA_Pause model.runState}
        match model.runState with 
        | Some r when r.chatMode.IsCM_Voice -> 
            let callId = RunState.lastFunctionCallId model.runState
            let asstMsg = RunState.lastAssistantMessage model.runState
            let conn = RunState.voiceConnection model.runState
            match conn,asstMsg,callId with
            | Some cnn, Some m, Some callId -> VoiceAsst.sendFunctionResponse cnn m.content callId
            | None,_,_ ->  failwith "no voice connection"
            | _,None,_ -> failwith  "no assistant message as the last message of chat"
            | _,_,None -> failwith "no function call id found to respond to voice assistant"
            model, Cmd.none
        | _ -> model,Cmd.none                 
        
    ///Either the voice assistant, the reasoning model or the user has submitted a question (i.e. a response)
    ///Send that to the CUA assitant to continue the task
    let resumeChat (model:Model) = 
        let question = RunState.question model.runState
        let prevCuaId = model.runState |> RunState.lastCuaResponse |> Option.bind(_.previous_response_id)
        let model =
            {model with 
                runState = 
                    model.runState 
                    |> RunState.appendChatMsg (User question)
                    |> RunState.setQuestion ""
                    |> RunState.setState CUA_Loop}
        match model.runState with
        | Some rs when rs.chatMode.IsCM_Text -> ComputerUse.sendTextResponse rs.bus (prevCuaId,question) |> Async.Start
        | Some rs when rs.chatMode.IsCM_Voice -> VoiceAsst.sendVoiceInstructions rs.bus (prevCuaId,question) |> Async.Start
        | _ -> ()
        model,Cmd.none   
        
    let p2pMap = function
        | P2PFromClient.Client_Connected c  -> Browser_Connected {| pid=c.pid |}
        | P2PFromClient.Client_Disconnect -> Browser_SocketDisconnected
        | P2PFromClient.Client_UrlSet url -> Browser_UrlSet url

    let launchBrowser (model:Model) =        
        let dllPath = System.IO.Path.Combine(Environment.CurrentDirectory,@"..\..\..\..\FsOpBrowser\bin\Debug\net9.0\FsOpBrowser.dll")
        let dllPath = System.IO.Path.GetFullPath(dllPath)
        let si = System.Diagnostics.ProcessStartInfo() 
        si.FileName <- "dotnet"
        si.Arguments <- $""" "{dllPath}" {model.browserState.port}"""
        let pd = new System.Diagnostics.Process()
        pd.StartInfo <- si
        pd.EnableRaisingEvents <- true
        pd.Exited.Add(fun _ -> 
            let msg = $"Browser process exited with code {pd.ExitCode}"
            Log.info msg           
            pd.Dispose()
            model.mailbox.Writer.TryWrite(Browser_ProcessExited) |> ignore)           
        pd.Start()


    let startP2pServer (model:Model) =
        async {
            let bst = model.browserState
            let poster = p2pMap >> model.mailbox.Writer.TryWrite >> ignore
            let listener = P2p.startServer bst.port bst.tokenSource.Token poster bst.outChannel            
            return Some {bst with listener = Some listener}
        }
        
    let startBrowser model =
        async {
            let started = launchBrowser model
            if not started then
                return failwith "Failed to start browser"
            return! startP2pServer model
        }
        
    let restartBrowser model   =
        async {        
            model.browserState.listener |> Option.iter (fun l -> l.Dispose())
            let started = launchBrowser model
            if not started then
                return failwith "Failed to start browser"
            let bst = model.browserState
            model.browserState.tokenSource.Cancel()
            do! Async.Sleep 1000
            let bst = {bst with tokenSource = new System.Threading.CancellationTokenSource()}
            let model = {model with browserState = bst}
            return! startP2pServer model
        }

    let browserPostUrl model =
        model.browserState.outChannel.Writer.TryWrite(P2PFromServer.Server_SetUrl model.url) |> ignore

    let setUrl model (origUrl:string) =
        let url = 
            if Uri.IsWellFormedUriString(origUrl, UriKind.Absolute) then
                origUrl
            else
                Uri("https://"+origUrl).ToString()
        if Uri.IsWellFormedUriString(url, UriKind.Absolute) then
            let m = {model with url = url; browserState.state = BST_AwaitAck}
            browserPostUrl m
            m, Cmd.none
        else
           model, Cmd.ofMsg (StatusMsg_Set $"Invalid URL '{origUrl}'") 
          
    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Error exn -> Log.exn(exn,""); model, Cmd.ofMsg (Abort (Some exn,""))
            | Initialize -> model, Cmd.OfAsync.either startBrowser model Browser_Started Error
            | InitializeDevMode -> model, Cmd.OfAsync.either startP2pServer model Browser_Started Error

            | Browser_Connected c  -> browserPostUrl model; {model with browserState.pid = Some c.pid; browserState.state=BST_AwaitAck}, Cmd.none
            | Browser_ProcessExited -> model, Cmd.OfAsync.either restartBrowser model Browser_Started Error
            | Browser_SocketDisconnected -> model, Cmd.none //browser exited signal seems to be stronger so just handle that
            | Browser_Started None -> model, Cmd.none
            | Browser_Started (Some bst) ->  {model with browserState = bst}, Cmd.none
            | Browser_UrlSet url -> {model with browserState.state = BST_Ready}, Cmd.none

            | TextChat_StartStopTask -> startStopForTextChat model
            | SetInstructions txt -> {model with instructions=Instructions.setTextChat txt model.instructions}, Cmd.none
            | Chat_UpdateQuestion txt -> {model with runState = RunState.setQuestion txt model.runState}, Cmd.none            
            | Chat_Append msg -> {model with runState = RunState.appendChatMsg msg model.runState}, Cmd.none
            | Chat_HandleTurnEnd -> handleTurnEnd model
            | Chat_Submit ->  resumeChat model             

            | AppendLog txt -> {model with log = (txt:: model.log) |> List.truncate 100}, Cmd.none
            | ClearLog -> {model with log = []}, Cmd.none
            | SetUrl txt -> setUrl model txt

            | SetAction txt -> {model with action=txt}, Cmd.ofMsg (FlashAction true)
            | FlashAction isOn -> {model with isFlashing = isOn}, if isOn then Cmd.OfAsync.perform delayFlash (not isOn) FlashAction else Cmd.none
            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear

            | TurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Abort (ex,msg) -> {model with runState = RunState.stop model.runState}, Cmd.ofMsg (StatusMsg_Set (ex |> Option.map _.Message |> Option.defaultValue msg))
            | TestSomething -> testSomething model

            | VoicChat_StartStop -> startStopForVoiceChat model

            | VoiceChat_RunInstructions (instructions,ev) ->
                match model.runState with 
                | Some rs when rs.chatMode.IsCM_Voice -> 
                    rs.lastFunctionCallId.Value <- Some ev
                    VoiceAsst.sendVoiceInstructions rs.bus (None,instructions) |> Async.Start
                | _ -> ()
                model, Cmd.none

            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex 
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

