namespace FsOperator
open System
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
        Browser.scroll (100,100) (0,700) |> Async.Start
        //Browser.pressKeys ["PageDown"] |> Async.Start       
        Log.info "testSomething clicked"
        model,Cmd.none

    let init _   =
        let model = {
            opTask = OpTask.empty
            isDirty = false
            taskState = None
            mailbox = mailbox
            log = []
            action = ""
            statusMsg = None,""
            browserMode = BM_Init
            isFlashing = false
        }        
        model,Cmd.ofMsg InitializeExternalBrowser

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
        let runState = {TaskState.initForText model.mailbox model.opTask with cuaState = CUA_Loop}
        ComputerUse.startApiMessaging (runState.tokenSource.Token,runState.bus)
        ComputerUse.sendStartMessage runState.bus (model.opTask.textModeInstructions) |> Async.Start
        ComputerUse.startCuaLoop runState 
        {model with taskState =  Some runState}, Cmd.none

    let stopTextLoop model = 
        {model with taskState = TaskState.stop model.taskState}, Cmd.none

    ///start or stop text mode task
    let startStopTextChat model =
        let cuaMode = TaskState.cuaMode model.taskState
        let cmMode = TaskState.chatMode model.taskState
        match cuaMode, cmMode with 
        | CUA_Init,  _ -> startTextLoop model
        | _ , CM_Text _ -> stopTextLoop model
        | _,_ -> model, Cmd.none

    let stopVoiceChat (model:Model) = 
        TaskState.voiceConnection model.taskState |> Option.iter VoiceMachine.stopVoiceMachine
        {model with taskState = TaskState.stop model.taskState}, Cmd.none

       ///start or stop voice mode task
    let startVoiceChat (model:Model) = 
        let runState = {TaskState.initForVoice model.mailbox model.opTask with cuaState = CUA_Loop}
        let model = {model with taskState = Some runState}
        match TaskState.voiceConnection model.taskState with
        | Some conn -> model, Cmd.OfAsync.either VoiceMachine.startVoiceMachine conn Nop Error
        | None -> model,Cmd.none        

    let startStopVoiceChat (model:Model) = 
        let cuaMode = TaskState.cuaMode model.taskState
        let cmMode = TaskState.chatMode model.taskState
        match cuaMode, cmMode with 
        | _, CM_Voice _ -> stopVoiceChat model
        | CUA_Init, _ -> startVoiceChat model
        | _,_ -> model, Cmd.none

    ///cua assistant loop has stopped and we need to respond to the assistant's last message (and chatHistory)
    ///For voice mode we send the cua assistant's last message to the voice assistant
    ///For text, no action required (for now until reasoning is enabled) the user can see the message and respond 
    let handleTurnEnd model =
        let model = {model with taskState = TaskState.setState CUA_Pause model.taskState}
        match model.taskState with 
        | Some r when r.chatMode.IsCM_Voice -> 
            let callId = TaskState.lastFunctionCallId model.taskState
            let asstMsg = TaskState.lastAssistantMessage model.taskState
            let conn = TaskState.voiceConnection model.taskState
            match conn,asstMsg,callId with
            | Some cnn, Some m, Some callId -> 
                r.lastFunctionCallId.Value <- None
                let cnn = match cnn.Value with Some c when c.WebRtcClient.State.IsConnected -> c | _ -> failwith "no voice connection"
                VoiceAsst.sendFunctionResponse cnn callId m.content
            | None,_,_ ->  failwith "no voice connection"
            | _,None,_ -> failwith  "no assistant message as the last message of chat"
            | _,_,None -> failwith "no function call id found to respond to voice assistant"
            model, Cmd.none
        | _ -> model,Cmd.none                 
        
    ///Either the voice assistant, the reasoning model or the user has submitted a question (i.e. a response)
    ///Send that to the CUA assitant to continue the task
    let resumeTextCuaLoop (model:Model) = 
        let question = TaskState.question model.taskState
        let model =
            match model.taskState with
            | Some rs when rs.chatMode.IsCM_Text -> 
                        
                let model =
                    {model with 
                        taskState = 
                            model.taskState 
                            |> TaskState.appendChatMsg (User question)
                            |> TaskState.setQuestion ""
                            |> TaskState.setState CUA_Loop}

                let chatMode = TaskState.chatMode model.taskState
                let history = ComputerUse.toChatHistory chatMode
                ComputerUse.sendTextResponse rs.bus history |> Async.Start
                ComputerUse.startCuaLoop model.taskState.Value 
                model
            | _ -> model
        model,Cmd.none   

    let startOrResumeVoiceCuaLoop (model:Model) instrFromVoiceAsst funcCallId =
        match model.taskState with 
        | Some rs when rs.chatMode.IsCM_Voice -> 
            rs.lastFunctionCallId.Value <- Some funcCallId 
            let prevVoiceState = TaskState.chatMode model.taskState |> function | CM_Voice v -> v | _ -> failwith "resumeVoiceChat: not a voice chat mode"
            let voiceState = 
                match prevVoiceState.chat.messages with 
                | [] -> {prevVoiceState with chat.systemMessage = Some instrFromVoiceAsst }
                | msgs -> {prevVoiceState with chat.messages = msgs @ [User instrFromVoiceAsst]}
            let model =
                {model with 
                    taskState = 
                        model.taskState 
                        |> TaskState.setMode (CM_Voice voiceState)
                        |> TaskState.setState CUA_Loop}

            match prevVoiceState.chat.messages with 
            | [] -> ComputerUse.sendStartMessage rs.bus instrFromVoiceAsst |> Async.Start   // for first instruction from voice assistant treat as the instructions to start the CUA loop
            | _  -> //otherwise resume the chat 
                let chatMode = TaskState.chatMode model.taskState
                let history = ComputerUse.toChatHistory chatMode
                ComputerUse.sendTextResponse rs.bus history |> Async.Start
            ComputerUse.startCuaLoop model.taskState.Value
            model,Cmd.none
        | _ -> model, Cmd.none
        
    let browserPostUrl model = Browser.postUrl model.opTask.url |> Async.Start //model.browserMode

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
            let m = {model with opTask = OpTask.setUrl url model.opTask}
            //let m = {model with opTask = OpTask.setUrl url model.opTask;  browserMode = BrowserMode.setEmbState BST_AwaitAck model.browserMode}
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

    let abort (model:Model) (ex:exn option) msg =
        match ex with Some ex -> Log.exn(ex,msg) | None -> Log.error msg
        let stopCmd = 
            match TaskState.chatMode model.taskState with
            | CM_Text _ -> Cmd.ofMsg TextChat_StartStopTask
            | CM_Voice _ -> Cmd.ofMsg VoicChat_StartStop
            | _ -> Cmd.none
        let statusCmd = Cmd.ofMsg (StatusMsg_Set (ex |> Option.map _.Message |> Option.defaultValue msg))
        let msgs = Cmd.batch [stopCmd; statusCmd]
        model,msgs
          
    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | InitializeExternalBrowser -> model, Cmd.OfAsync.either Browser.launchExternal () Browser_Connected Error
            | Browser_Connected _  -> browserPostUrl model;  {model with browserMode = BM_Ready}, Cmd.none

            | OpTask_SetTextInstructions txt -> let isDirty = txt <> model.opTask.textModeInstructions in {model with opTask=OpTask.setTextPrompt txt model.opTask}, Cmd.ofMsg (OpTask_MarkDirty isDirty)
            | OpTask_Update instr -> let isDirty = instr <> model.opTask in {model with opTask=instr}, Cmd.ofMsg (OpTask_MarkDirty isDirty)
            | OpTask_MarkDirty isDirty -> let m = {model with isDirty = isDirty} in setTitle win m; m, Cmd.none
            | OpTask_SetUrl txt -> setUrl model txt
            | OpTask_Load when (TaskState.cuaMode model.taskState).IsCUA_Init -> model, Cmd.OfAsync.either loadTask (win,model) OpTask_Loaded Error
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
            | Abort (ex,msg) -> abort model ex msg
            | TestSomething -> testSomething model

            | Chat_CUATurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Chat_UpdateQuestion txt -> {model with taskState = TaskState.setQuestion txt model.taskState}, Cmd.none            
            | Chat_Append msg -> {model with taskState = TaskState.appendChatMsg msg model.taskState}, Cmd.none
            | Chat_HandleTurnEnd -> handleTurnEnd model
            | Chat_Resume ->  resumeTextCuaLoop model             

            | TextChat_StartStopTask -> startStopTextChat model
            | VoicChat_StartStop -> startStopVoiceChat model
            | VoiceChat_RunInstructions (instructions,ev) -> startOrResumeVoiceCuaLoop model instructions ev
            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex 
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

