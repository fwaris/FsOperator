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
        let m = NativeDriver.create("olk")
        let m = NativeDriver.create("dotnet")
        let s,(w,h) = m.driver.snapshot() |> Async.RunSynchronously

        
        //Browser.pressKeys ["PageDown"] |> Async.Start
        Log.info "testSomething clicked"
        FsResponses.Log.debug_logging <- not FsResponses.Log.debug_logging
        model,Cmd.none

    let init _   =
        let ui = PlaywrightDriver.create()
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
            ui = ui
            driver = ui.driver
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

    let startTextChat model =
        if isEmpty model.opTask.textModeInstructions then failwith "No instructions provided for text mode task"
        if isEmpty model.opTask.url then failwith "No URL provided for text mode task"
        let runState = {TaskState.initForText model.mailbox model.opTask with cuaState = CUA_Loop}
        ComputerUse.startApiMessaging (runState.tokenSource.Token,runState.bus)
        ComputerUse.sendStartMessage model.driver runState.bus (model.opTask.textModeInstructions) |> Async.Start
        ComputerUse.startCuaLoop model.driver runState
        {model with taskState =  Some runState}, Cmd.none

    let stopTextChat model =
        {model with taskState = TaskState.stop model.taskState}, Cmd.none

    ///start or stop text mode task
    let startStopTextChat model =
        let cuaMode = TaskState.cuaMode model.taskState
        let cmMode = TaskState.chatMode model.taskState
        match cuaMode, cmMode with
        | CUA_Init,  _ -> startTextChat model
        | _ , CM_Text _ -> stopTextChat model
        | _,_ -> model, Cmd.none

    let stopVoiceChat (model:Model) =
        match TaskState.chatMode model.taskState with
        | CM_Voice _ ->
            TaskState.voiceConnection model.taskState |> VoiceMachine.stopVoiceMachine
            {model with taskState = TaskState.stop model.taskState}, Cmd.none
        | _ -> model, Cmd.none

       ///start or stop voice mode task
    let startVoiceChat (model:Model) =
        let ts = {TaskState.initForVoice model.mailbox model.opTask with cuaState = CUA_Loop}
        let model = {model with taskState = Some ts}
        ComputerUse.startApiMessaging (ts.tokenSource.Token,ts.bus)
        ComputerUse.startCuaLoop model.driver ts
        model, Cmd.OfAsync.either VoiceMachine.startVoiceMachine ts Nop Error

    let startStopVoiceChat (model:Model) =
        let cuaMode = TaskState.cuaMode model.taskState
        let cmMode = TaskState.chatMode model.taskState
        match cuaMode, cmMode with
        | CUA_Init, _ -> startVoiceChat model
        | _, CM_Voice _ -> stopVoiceChat model
        | _,_ -> model, Cmd.none

    ///cua assistant loop has stopped and we need to respond to the assistant's last message (and chatHistory)
    ///For voice mode we send the cua assistant's last message to the voice assistant
    ///For text, no action required (for now until reasoning is enabled) the user can see the message and respond
    let handleTurnEnd model =
        match TaskState.cuaMode model.taskState with
        | CUA_Loop  ->
            let model = {model with taskState = TaskState.setCuaMode CUA_Pause model.taskState}
            match model.taskState with
            | Some r when r.chatMode.IsCM_Voice ->
                let callId = TaskState.functionId VoiceMachine.ASST_INSTRUCTIONS_FUNCTION r
                let lastAsstMsg = TaskState.lastAssistantMessage model.taskState
                let instrForVoice =
                    match lastAsstMsg with
                    | Some m -> m.content
                    | None -> "Assistant completed the task but did not generate a text response"
                let conn = TaskState.voiceConnection model.taskState
                match callId with
                | Some callId ->
                    let conn = match conn.Value with Some c when c.WebRtcClient.State.IsConnected -> c | _ -> failwith "no connection to send"
                    TaskState.removeFunctionId VoiceMachine.ASST_INSTRUCTIONS_FUNCTION r
                    let image = TaskState.screenshots model.taskState |> List.tryHead
                    VoiceAsst.sendFunctionResponseWithImage conn callId instrForVoice image |> Async.Start
                | None -> failwith "no function call id found to respond to voice assistant"
                model, Cmd.none
            | _ -> model,Cmd.none
        | _ -> model,Cmd.none

    ///User submitted a prompt in response to CUA text response - resume CUA after this
    let resumeTextCuaLoop (model:Model) =
        let question = TaskState.question model.taskState
        match TaskState.cuaMode model.taskState with
        | CUA_Pause ->
            let model =
                match model.taskState with
                | Some rs when rs.chatMode.IsCM_Text ->

                    let model =
                        {model with
                            taskState =
                                model.taskState
                                |> TaskState.appendChatMsg (User question)
                                |> TaskState.setQuestion ""
                                |> TaskState.setCuaMode CUA_Loop}

                    let chatMode = TaskState.chatMode model.taskState
                    let instr,messages = ComputerUse.toChatHistory chatMode
                    let messages = ComputerUse.truncateHistory messages
                    ComputerUse.sendTextResponse model.driver rs.bus (instr,messages) |> Async.Start
                    ComputerUse.startCuaLoop model.driver model.taskState.Value
                    model
                | _ -> model

            model,Cmd.none
        |_ -> model,Cmd.none

    ///User submitted a prompt in response to CUA text response - resume CUA after this
    let startOrResumeVoiceCuaLoop (model:Model) instrFromVoiceAsst funcCallId =
        match model.taskState with
        | Some rs when rs.chatMode.IsCM_Voice ->
            TaskState.setFunctionId VoiceMachine.ASST_INSTRUCTIONS_FUNCTION funcCallId rs
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
                        |> TaskState.setCuaMode CUA_Loop}
            match prevVoiceState.chat.messages with
            | [] -> ComputerUse.sendStartMessage model.driver rs.bus instrFromVoiceAsst |> Async.Start   // for first instruction from voice assistant treat as the instructions to start the CUA loop
            | _  -> //otherwise resume the chat
                let chatMode = TaskState.chatMode model.taskState
                let history = ComputerUse.toChatHistory chatMode
                ComputerUse.sendTextResponse model.driver rs.bus history |> Async.Start
            ComputerUse.startCuaLoop model.driver model.taskState.Value
            model,Cmd.none
        | _ -> model, Cmd.none

    let browserPostUrl model =
        match model.ui with
        | Pw u -> u.postUrl model.opTask.url |> Async.Start //model.browserMode
        | _ -> ()

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
            let isDirty = prevUrl <> m.opTask.url
            let syncCmd = Cmd.ofMsg SyncUrlToBrowser
            let cmds =
                if isDirty then
                    Cmd.batch (Cmd.ofMsg OpTask_MarkDirty::syncCmd::[])
                else
                    Cmd.none
            m,cmds
        | None -> model, Cmd.ofMsg (StatusMsg_Set $"Invalid URL '{origUrl}'")

    let syncUrl model =
        match TaskState.cuaMode model.taskState, TaskState.chatMode model.taskState with
        | CUA_Init, CM_Init -> browserPostUrl model; model, Cmd.none
        | _, CM_Voice _ -> browserPostUrl model; model, Cmd.none
        | CUA_Loop, CM_Text _ -> model, Cmd.ofMsg (StatusMsg_Set "Cannot sync url to browser in current state")
        | _,_ -> model,Cmd.none

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

    let serializeTask (file:string) (opTask:OpTask) =
        use strw = System.IO.File.Create file
        let opTask = OpTask.setId file opTask
        (System.Text.Json.JsonSerializer.Serialize<OpTask>(strw,opTask))
        opTask

    let saveTaskAs (win:HostWindow, opTask:OpTask) =
        async {
            let! file = Dialogs.saveFileDialog win (Some opTask.id)
            let rslt =
                match file with
                | Some file -> Some (serializeTask file opTask)
                | None -> None
            return rslt
        }

    let saveTask (win:HostWindow, opTask:OpTask) =
        async {
            if IO.File.Exists opTask.id then
                let rslt = Some (serializeTask opTask.id opTask)
                return rslt
            else
                let! rslt = saveTaskAs (win,opTask)
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
        let model,stopCmd =
            match TaskState.chatMode model.taskState with
            | CM_Text _ -> stopTextChat model
            | CM_Voice _ -> stopVoiceChat model
            | _ -> model,Cmd.none
        let statusCmd = Cmd.ofMsg (StatusMsg_Set (ex |> Option.map _.Message |> Option.defaultValue msg))
        let msgs = Cmd.batch [stopCmd; statusCmd]
        model,msgs

    let updateTask model (instr:OpTask) =
        let task =
            {model.opTask with
                url=instr.url
                description=instr.description
                textModeInstructions=instr.textModeInstructions
                voiceAsstInstructions=instr.voiceAsstInstructions
            }
        let isDirty = task <> model.opTask
        let model = {model with opTask=task}
        let updtdMsg = Cmd.ofMsg (StatusMsg_Set $"Updated '{task.id}'")
        if isDirty then
            let cmds = [
                Cmd.ofMsg OpTask_MarkDirty
                Cmd.ofMsg SyncUrlToBrowser
                updtdMsg
            ]
            model, Cmd.batch cmds
        else
            model,updtdMsg

    let setInstructions model txt =
        let isDirty = txt <> model.opTask.textModeInstructions
        {model with opTask=OpTask.setTextPrompt txt model.opTask}, if isDirty then  Cmd.ofMsg OpTask_MarkDirty else Cmd.none

    let clearAll model =
         let m,_ = stopTextChat model
         let m,_ = stopVoiceChat m
         {m with opTask=OpTask.empty; taskState=None; action=""},
         Cmd.batch [
            Cmd.ofMsg (StatusMsg_Set "cleared")
            Cmd.ofMsg OpTask_ClearDirty
         ]

    let startBrowser model =
        model, Cmd.batch [
            Cmd.ofMsg (StatusMsg_Set "Staring browser..." );
            Cmd.OfAsync.either PlaywrightDriver.launchExternal () Browser_Connected Error]

    let stopAndSummarize model =
        {model with taskState = TaskState.setCuaMode CUA_Loop_Closing model.taskState},
        Cmd.OfAsync.either ComputerUse.summarizeProgressCua (model.driver,model.taskState.Value) Chat_GotSummary_Cua Error

    let reportProgress model (id,cntnt,isCuaResp) =
        if isCuaResp then //(isCuaResp && isEmpty cntnt then                          //cua model did not generate a summary response, try alt model
            let chatMode = TaskState.chatMode model.taskState
            let instr,messages = ComputerUse.toChatHistory chatMode
            let messages = ComputerUse.truncateHistory messages
            let screenshots = TaskState.screenshots model.taskState
            let cmd1 = Cmd.ofMsg (StatusMsg_Set "Generating report using alt. model ...")
            let cmd2 = Cmd.OfAsync.either ComputerUse.summarizeProgressReasoner (instr,messages,screenshots) Chat_GotSummary_Alt Error
            model, Cmd.batch [cmd1; cmd2]
        else
            let m = {model with taskState = TaskState.appendChatMsg (Assistant {id=id; content=cntnt}) model.taskState}
            match TaskState.chatMode m.taskState with
            | CM_Text _ -> stopTextChat m
            | CM_Voice _ -> stopVoiceChat m
            | _          -> m,Cmd.none

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | InitializeExternalBrowser -> startBrowser model
            | Browser_Connected _  -> browserPostUrl model;  {model with browserMode = BM_Ready}, Cmd.none

            | OpTask_SetTextInstructions txt -> setInstructions model txt
            | OpTask_Update instr -> updateTask model instr
            | OpTask_MarkDirty -> let m = {model with isDirty = true} in setTitle win m; m, Cmd.none
            | OpTask_ClearDirty -> let m = {model with isDirty = false} in setTitle win m; m, Cmd.none
            | OpTask_SetUrl txt -> setUrl model txt
            | OpTask_Load when (TaskState.cuaMode model.taskState).IsCUA_Init -> model, Cmd.OfAsync.either loadTask (win,model) OpTask_Loaded Error
            | OpTask_Load -> model,Cmd.none
            | OpTask_Loaded (Some instr) -> {model with opTask=instr}, Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg SyncUrlToBrowser]
            | OpTask_Loaded None -> model, Cmd.none
            | OpTask_LoadSample sample -> model, Cmd.OfAsync.either checkLoadSample (win,model,sample) OpTask_Loaded Error
            | OpTask_Save -> model, Cmd.OfAsync.either saveTask (win,model.opTask) OpTask_Saved Error
            | OpTask_SaveAs -> model, Cmd.OfAsync.either saveTaskAs (win,model.opTask) OpTask_Saved Error
            | OpTask_Saved (Some t) -> {model with opTask=t},Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg (StatusMsg_Set $"saved {t.id}")]
            | OpTask_Saved None -> model, Cmd.none
            | OpTask_Clear -> clearAll model

            | SyncUrlToBrowser -> syncUrl model

            | Action_Set txt -> {model with action=txt}, Cmd.ofMsg (Action_Flash true)
            | Action_Flash isOn -> {model with isFlashing = isOn}, if isOn then Cmd.OfAsync.perform delayFlash (not isOn) Action_Flash else Cmd.none

            | Log_Append txt -> {model with log = (txt:: model.log) |> List.truncate 10}, Cmd.none
            | Log_Clear -> {model with log = []}, Cmd.none

            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear
            | Error exn -> Log.exn(exn,""); model, Cmd.ofMsg (Abort (Some exn,""))
            | Abort (ex,msg) -> abort model ex msg
            | TestSomething -> testSomething model
            | Nop _ -> model, Cmd.none

            | Chat_CUATurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Chat_UpdateQuestion txt -> {model with taskState = TaskState.setQuestion txt model.taskState}, Cmd.none
            | Chat_Append msg -> {model with taskState = TaskState.appendChatMsg msg model.taskState}, Cmd.none
            | Chat_HandleTurnEnd -> handleTurnEnd model
            | Chat_Resume ->  resumeTextCuaLoop model
            | Chat_StopAndSummarize -> stopAndSummarize model
            | Chat_GotSummary_Cua (id,cntnt) -> reportProgress model (id,cntnt,true)
            | Chat_GotSummary_Alt (id,cntnt) -> reportProgress model (id,cntnt,false)

            | TextChat_StartStopTask -> startStopTextChat model
            | VoiceChat_StartStop -> startStopVoiceChat model
            | VoiceChat_RunInstructions (instructions,ev) -> startOrResumeVoiceCuaLoop model instructions ev
            //| _ -> model, Cmd.none
        with ex ->
            printfn "%A" ex
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

