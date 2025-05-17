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
                Assistant {id = "1"; content = model.opTask.textModeInstructions}
                User "The quick brown fox jumped over the lazy dog"
                Assistant {id = "2"; content = "How can I help you?"}
            ]
        let runState = RunState.Create model.mailbox model.opTask
        let runState = {runState with chatMode = CM_Text {Chat.Default with messages=testChat} ; cuaState = CUAState.CUA_Pause}
        {model with runState =  Some runState}, Cmd.none


    let init _   =
        //FsResponses.debug_logging <- true
        //let sample = Instructions.sampleNetflix
        let sample = OpTask.sample
        let model = {
            opTask = sample
            isDirty = false
            runState = None
            mailbox = Channel.CreateBounded(10)
            log = []
            action = ""
            statusMsg = None,""
            browserMode = Embedded (BrowserAppState.Create())
            isFlashing = false
        }        
        model,Cmd.ofMsg Initialize
        //model,Cmd.ofMsg InitializeDevMode
        //model,Cmd.ofMsg InitializeExternalBrowser

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

    let startTextLoop model = 
        let runState = {RunState.initForText model.mailbox model.opTask with cuaState = CUA_Loop}
        ComputerUse.startApiMessaging (runState.tokenSource.Token,runState.bus)
        ComputerUse.sendStartMessage runState.bus (model.opTask.textModeInstructions) |> Async.Start
        ComputerUse.startCuaLoop runState 
        {model with runState =  Some runState}, Cmd.none

    ///start or stop text mode task
    let startStopTextChat model =
        match model.runState with 
        | Some r when (r.cuaState.IsCUA_Init 
                       && BrowserMode.isReady model.browserMode) -> startTextLoop model
        | None when BrowserMode.isReady model.browserMode -> startTextLoop model
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Text && not runState.cuaState.IsCUA_Init -> 
            {model with runState = RunState.stop model.runState}, Cmd.none
        | _ -> model, Cmd.none //ignore any other state

    let startVoiceLoop model = 
        let runState = {RunState.initForVoice model.mailbox model.opTask with cuaState = CUA_Loop}
        ComputerUse.startApiMessaging (runState.tokenSource.Token, runState.bus)
        VoiceMachine.startVoiceMachine runState |> Async.Start
        {model with runState =  Some runState}, Cmd.none

    ///start or stop voice mode task
    let startStopVoiceChat (model:Model) = 
        match model.runState with 
        | Some r when  (r.cuaState.IsCUA_Init 
                       && BrowserMode.isReady model.browserMode) -> startVoiceLoop model
        | None when BrowserMode.isReady model.browserMode -> startVoiceLoop model
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Voice -> 
            VoiceMachine.stopVoiceMachine runState |> Async.Start
            {model with runState = RunState.stop model.runState}, Cmd.none
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
            | Some cnn, Some m, Some callId -> 
                r.lastFunctionCallId.Value <- None
                VoiceAsst.sendFunctionResponse cnn callId m.content
            | None,_,_ ->  failwith "no voice connection"
            | _,None,_ -> failwith  "no assistant message as the last message of chat"
            | _,_,None -> failwith "no function call id found to respond to voice assistant"
            model, Cmd.none
        | _ -> model,Cmd.none                 
        
    ///Either the voice assistant, the reasoning model or the user has submitted a question (i.e. a response)
    ///Send that to the CUA assitant to continue the task
    let resumeTextCuaLoop (model:Model) = 
        let question = RunState.question model.runState
        let model =
            match model.runState with
            | Some rs when rs.chatMode.IsCM_Text -> 
                        
                let model =
                    {model with 
                        runState = 
                            model.runState 
                            |> RunState.appendChatMsg (User question)
                            |> RunState.setQuestion ""
                            |> RunState.setState CUA_Loop}

                let chatMode = RunState.chatMode model.runState
                let history = ComputerUse.toChatHistory chatMode
                ComputerUse.sendTextResponse rs.bus history |> Async.Start
                ComputerUse.startCuaLoop model.runState.Value 
                model
            | _ -> model
        model,Cmd.none   

    let startOrResumeVoiceCuaLoop (model:Model) instrFromVoiceAsst funcCallId =
        match model.runState with 
        | Some rs when rs.chatMode.IsCM_Voice -> 
            rs.lastFunctionCallId.Value <- Some funcCallId 
            let prevVoiceState = RunState.chatMode model.runState |> function | CM_Voice v -> v | _ -> failwith "resumeVoiceChat: not a voice chat mode"
            let voiceState = 
                match prevVoiceState.chat.messages with 
                | [] -> {prevVoiceState with chat.systemMessage = Some instrFromVoiceAsst }
                | msgs -> {prevVoiceState with chat.messages = msgs @ [User instrFromVoiceAsst]}
            let model =
                {model with 
                    runState = 
                        model.runState 
                        |> RunState.setMode (CM_Voice voiceState)
                        |> RunState.setState CUA_Loop}

            match prevVoiceState.chat.messages with 
            | [] -> ComputerUse.sendStartMessage rs.bus instrFromVoiceAsst |> Async.Start   // for first instruction from voice assistant treat as the instructions to start the CUA loop
            | _  -> //otherwise resume the chat 
                let chatMode = RunState.chatMode model.runState
                let history = ComputerUse.toChatHistory chatMode
                ComputerUse.sendTextResponse rs.bus history |> Async.Start
            ComputerUse.startCuaLoop model.runState.Value
            model,Cmd.none
        | _ -> model, Cmd.none
        
    let p2pMap = function
        | P2PFromClient.Client_Connected c  -> Browser_Connected {| pid=c.pid |}
        | P2PFromClient.Client_Disconnect -> Browser_Emb_SocketDisconnected
        | P2PFromClient.Client_UrlSet url -> Browser_Emb_UrlSet url

    let launchBrowser (model:Model) =        
        let dllPath = System.IO.Path.Combine(Environment.CurrentDirectory,@"..\..\..\..\FsOpBrowser\bin\Debug\net9.0\FsOpBrowser.dll")
        let dllPath = System.IO.Path.GetFullPath(dllPath)
        let si = System.Diagnostics.ProcessStartInfo() 
        si.FileName <- "dotnet"
        si.Arguments <- $""" "{dllPath}" {BrowserMode.port model.browserMode}"""
        let pd = new System.Diagnostics.Process()
        pd.StartInfo <- si
        pd.EnableRaisingEvents <- true
        pd.Exited.Add(fun _ -> 
            let msg = $"Browser process exited with code {pd.ExitCode}"
            Log.info msg           
            pd.Dispose()
            model.mailbox.Writer.TryWrite(Browser_Emb_ProcessExited) |> ignore)           
        pd.Start()

    let startP2pServer (model:Model) =
        async {
            let bst = match model.browserMode with Embedded bst -> bst | _ -> failwith "startP2pServer called without embedded browser mode"
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
            let bst = match model.browserMode with Embedded bst -> bst | _ -> failwith "startP2pServer called without embedded browser mode"
            bst.listener |> Option.iter (fun l -> l.Dispose())
            let started = launchBrowser model
            if not started then
                return failwith "Failed to start browser"
            bst.tokenSource.Cancel()
            do! Async.Sleep 1000
            let bst = {bst with tokenSource = new System.Threading.CancellationTokenSource()}
            let model = {model with browserMode = Embedded bst}
            return! startP2pServer model
        }

    let browserPostUrl model = BrowserMode.postUrl model.opTask.url model.browserMode

    let checkUrl (url:string) =
        if Uri.IsWellFormedUriString(url, UriKind.Absolute) then
            Some url
        else
            let url = "https://"+url
            if Uri.IsWellFormedUriString(url, UriKind.Absolute) then 
                Some url
            else 
                None

    let setUrl model (origUrl:string) =
        match checkUrl origUrl with
        | Some url -> 
            let m = {model with opTask = OpTask.setUrl url model.opTask;  browserMode = BrowserMode.setEmbState BST_AwaitAck model.browserMode}
            browserPostUrl m
            m, Cmd.ofMsg (OpTask_MarkDirty true)
        | None -> model, Cmd.ofMsg (StatusMsg_Set $"Invalid URL '{origUrl}'")

    let setTitle (win:HostWindow) model =
        let dirty = if model.isDirty then "*" else ""
        let title = $"{C.WIN_TITLE} - {model.opTask.id}{dirty}"
        win.Title <- title
          
    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> setTitle win model; model, Cmd.OfAsync.either startBrowser model Browser_Emb_Started Error
            | InitializeDevMode -> setTitle win model; model, Cmd.OfAsync.either startP2pServer model Browser_Emb_Started Error
            | InitializeExternalBrowser ->setTitle win model; {model with browserMode = External {|pid=None|}}, Cmd.OfAsync.either Browser.launchExternal () Browser_Connected Error

            | Browser_Connected c  -> browserPostUrl model;  {model with browserMode = model.browserMode |>  BrowserMode.setPid c.pid |> BrowserMode.setEmbState BST_AwaitAck}, Cmd.none
            | Browser_Emb_ProcessExited -> model, Cmd.OfAsync.either restartBrowser model Browser_Emb_Started Error
            | Browser_Emb_SocketDisconnected -> model, Cmd.none //browser exited signal seems to be stronger so just handle that
            | Browser_Emb_Started None -> model, Cmd.none
            | Browser_Emb_Started (Some bst) ->  {model with browserMode = BrowserMode.setEmbAppState bst model.browserMode}, Cmd.none
            | Browser_Emb_UrlSet url -> {model with browserMode = BrowserMode.setEmbState BST_Ready model.browserMode}, Cmd.none

            | OpTask_SetTextInstructions txt -> {model with opTask=OpTask.setTextPrompt txt model.opTask}, Cmd.ofMsg (OpTask_MarkDirty true)
            | OpTask_Update instr -> {model with opTask=instr}, Cmd.ofMsg (OpTask_MarkDirty true)
            | OpTask_MarkDirty isDirty -> let m = {model with isDirty = isDirty} in setTitle win m; m, Cmd.none
            | OpTask_SetUrl txt -> setUrl model txt
            | OpTask_Load -> model, Cmd.none
            | OpTask_Loaded instr -> {model with opTask=instr}, Cmd.ofMsg (OpTask_MarkDirty false)
            | OpTask_Save -> model, Cmd.ofMsg (OpTask_MarkDirty false)

            | Action_Set txt -> {model with action=txt}, Cmd.ofMsg (Action_Flash true)
            | Action_Flash isOn -> {model with isFlashing = isOn}, if isOn then Cmd.OfAsync.perform delayFlash (not isOn) Action_Flash else Cmd.none

            | Log_Append txt -> {model with log = (txt:: model.log) |> List.truncate 10}, Cmd.none
            | Log_Clear -> {model with log = []}, Cmd.none

            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear
            | Error exn -> Log.exn(exn,""); model, Cmd.ofMsg (Abort (Some exn,""))
            | Abort (ex,msg) -> {model with runState = RunState.stop model.runState}, Cmd.ofMsg (StatusMsg_Set (ex |> Option.map _.Message |> Option.defaultValue msg))
            | TestSomething -> testSomething model

            | Chat_CUATurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Chat_UpdateQuestion txt -> {model with runState = RunState.setQuestion txt model.runState}, Cmd.none            
            | Chat_Append msg -> {model with runState = RunState.appendChatMsg msg model.runState}, Cmd.none
            | Chat_HandleTurnEnd -> handleTurnEnd model
            | Chat_Resume ->  resumeTextCuaLoop model             

            | TextChat_StartStopTask -> startStopTextChat model
            | VoicChat_StartStop -> startStopVoiceChat model
            | VoiceChat_RunInstructions (instructions,ev) -> startOrResumeVoiceCuaLoop model instructions ev
            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex 
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

