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
open Avalonia.Threading

module Update =
    let mailbox = Channel.CreateBounded<ClientMsg>(10)

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
        Log.info "testSomething clicked"
        //async {
        //    try
        //        //do! Preview.drawArrow 100 500 50 0. 2000
        //        do! Preview.drawClick 100 500 2000
        //        //do! Preview.drawDragArrow (100,500) (300,600) 2000
        //        debug "Test something"
        //    with ex ->
        //        debug $"Error in testSomething: {ex.Message}"
        //}
        //|> Async.Start
        model,Cmd.none

    let init _   =
        //FsResponses.debug_logging <- true
        //let sample = Instructions.sampleNetflix
        //let sample = OpTask.sample
        let model = {
            opTask = OpTask.empty
            isDirty = false
            runState = None
            mailbox = mailbox
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
        if isEmpty model.opTask.textModeInstructions then failwith "No instructions provided for text mode task"
        if isEmpty model.opTask.url then failwith "No URL provided for text mode task" 
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
        if isEmpty model.opTask.url then failwith "No URL provided for voice mode task" 
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

    let startP2pServer (model:Model) =
        async {
            let bst = match model.browserMode with Embedded bst -> bst | _ -> failwith "startP2pServer called without embedded browser mode"
            let poster = p2pMap >> model.mailbox.Writer.TryWrite >> ignore
            let listener = P2p.startServer bst.port bst.tokenSource.Token poster bst.outChannel            
            return Some {bst with listener = Some listener}
        }
        
    let startBrowser model =
        async {
            let fnExit ()  = model.mailbox.Writer.TryWrite(Browser_Emb_ProcessExited) |> ignore
            let started = Browser.launchBrowser model.browserMode fnExit
            if not started then
                return failwith "Failed to start browser"
            return! startP2pServer model
        }
        
    let restartBrowser model   =
        async {
            let fnExit ()  = model.mailbox.Writer.TryWrite(Browser_Emb_ProcessExited) |> ignore
            let bst = match model.browserMode with Embedded bst -> bst | _ -> failwith "startP2pServer called without embedded browser mode"
            bst.listener |> Option.iter (fun l -> l.Dispose())
            let started = Browser.launchBrowser model.browserMode fnExit
            if not started then
                return failwith "Failed to start browser"
            bst.tokenSource.Cancel()
            do! Async.Sleep 1000
            let bst = {bst with tokenSource = new System.Threading.CancellationTokenSource()}
            let model = {model with browserMode = Embedded bst}
            return! startP2pServer model
        }

    let browserPostUrl model = Browser.postUrl model.opTask.url model.browserMode

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
        let prevUrl = model.opTask.url
        match checkUrl origUrl with
        | Some url -> 
            let m = {model with opTask = OpTask.setUrl url model.opTask;  browserMode = BrowserMode.setEmbState BST_AwaitAck model.browserMode}
            browserPostUrl m
            let isDirty = prevUrl <> m.opTask.url
            m, if isDirty then Cmd.ofMsg (OpTask_MarkDirty true) else Cmd.none
        | None -> model, Cmd.ofMsg (StatusMsg_Set $"Invalid URL '{origUrl}'")

    let setTitle (win:HostWindow) model =
        let dirty = if model.isDirty then "*" else ""
        let title = $"{C.WIN_TITLE} - {model.opTask.id}{dirty}"
        win.Title <- title

    let promptSave win =
        task {
            return!
                Dispatcher.UIThread.InvokeAsync<bool>(fun _ ->
                    task {
                        let dlg = YesNoDialog("Save current task before continuing?")
                        return! dlg.ShowDialogAsync(win)
                    })
        }

    let saveTask (win:HostWindow, opTask:OpTask) = 
        async {
            let! file = Dialogs.saveFileDialog win (Some opTask.id) 
            let rslt = 
                match file with
                | Some file -> 
                    use strw = System.IO.File.Create file
                    let opTask = OpTask.setId file opTask
                    do (System.Text.Json.JsonSerializer.Serialize<OpTask>(strw,opTask))
                    Some opTask
                | None -> None
            return rslt
        }

    let loadDlg win = async {
        match! Dialogs.openFileDialog win  with 
        | Some file -> 
            let opTask = 
                use str = System.IO.File.OpenRead file
                let t =  System.Text.Json.JsonSerializer.Deserialize<OpTask>(str)
                t
                |> OpTask.setId file
                |> OpTask.setVoicePrompt (fixEmpty t.voiceAsstInstructions)
            return Some opTask
        | None -> return None
    }
      
    let loadTask (win:HostWindow,model) = 
        async {
            if model.isDirty then 
                let! doSave = promptSave win |> Async.AwaitTask
                if doSave then                     
                    let! saveRsult = saveTask (win,model.opTask)
                    match saveRsult with
                    | None -> return None
                    | Some _ -> return! (loadDlg win)
                else return! (loadDlg win)
            else
                return! (loadDlg win)
        }    

    let checkLoadSample (win,model,sample) =
        async {
            if model.isDirty then 
                let! doSave = promptSave win |> Async.AwaitTask
                if doSave then                     
                    let! saveRsult = saveTask (win,model.opTask)
                    match saveRsult with
                    | None -> return None
                    | Some _ -> return (Some sample)
                else return (Some sample)
            else
                return (Some sample)
        }
          
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

            | OpTask_SetTextInstructions txt -> let isDirty = txt <> model.opTask.textModeInstructions in {model with opTask=OpTask.setTextPrompt txt model.opTask}, Cmd.ofMsg (OpTask_MarkDirty isDirty)
            | OpTask_Update instr -> let isDirty = instr <> model.opTask in {model with opTask=instr}, Cmd.ofMsg (OpTask_MarkDirty isDirty)
            | OpTask_MarkDirty isDirty -> let m = {model with isDirty = isDirty} in setTitle win m; m, Cmd.none
            | OpTask_SetUrl txt -> setUrl model txt
            | OpTask_Load when (RunState.cuaMode model.runState).IsCUA_Init -> model, Cmd.OfAsync.either loadTask (win,model) OpTask_Loaded Error
            | OpTask_Load -> model,Cmd.none
            | OpTask_Loaded (Some instr) -> {model with opTask=instr}, Cmd.batch [Cmd.ofMsg (OpTask_MarkDirty false); Cmd.ofMsg (OpTask_SetUrl instr.url)]
            | OpTask_Loaded None -> model, Cmd.none
            | OpTask_LoadSample sample -> model, Cmd.OfAsync.either checkLoadSample (win,model,sample) OpTask_Loaded Error
            | OpTask_Save -> model, Cmd.OfAsync.either saveTask (win,model.opTask) OpTask_Saved Error
            | OpTask_Saved (Some t) -> {model with opTask=t}, Cmd.ofMsg (OpTask_MarkDirty false)
            | OpTask_Saved None -> model, Cmd.none

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

