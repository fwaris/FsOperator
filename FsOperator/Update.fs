namespace FsOperator
open System
open System.Threading.Channels
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts
open FsOpCore
open Avalonia.Threading
open Microsoft.SemanticKernel
open Microsoft.Extensions.DependencyInjection

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
        //let m = NativeDriver.create("olk")
        let m = NativeDriver.create("dotnet") (Some "FsOperator")
        let p,a = match m with Na x -> x.processName, x.arg | _ -> failwith "not a native driver"
        let s,(w,h) = m.driver.snapshot () |> Async.RunSynchronously
        
        //Browser.pressKeys ["PageDown"] |> Async.Start
        Log.info "testSomething clicked"
        FsResponses.Log.debug_logging <- not FsResponses.Log.debug_logging
        model,Cmd.none

    let init _   =
        let ui = PlaywrightDriver.create()
        let model = {
            opTask = OpTask.empty
            isDirty = false
            mailbox = mailbox
            log = []
            action = ""
            statusMsg = None,""
            browserMode = BM_Init
            isFlashing = false
            ui = ui
            driver = ui.driver
            flow = Flow.Default
            voiceAsst = None
        }
        model,Cmd.none

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

    let browserPostUrl startBrowser (model:Model) =        
        if startBrowser || FsOpCore.PlaywrightDriver._connection.Value.IsSome then        
            match model.ui, model.opTask.target with
            | Pw u, TLink url -> u.postUrl url |> Async.Start //model.browserMode
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

    let setTarget model (tgt:string) = 
        let prevTarget = model.opTask.target
        let newTarget = OpTask.parseTarget tgt 
        let isDirty = prevTarget <> newTarget
        let model = {model with opTask = OpTask.setTarget newTarget model.opTask}
        let cmds = match newTarget with TLink url -> [Cmd.ofMsg (SyncUrlToBrowser true)] | _ -> []
        let cmds = if isDirty then (Cmd.ofMsg OpTask_MarkDirty::cmds) else cmds
        model, Cmd.batch cmds

    let syncUrl starBrowser model =
        browserPostUrl starBrowser model
        model,Cmd.none
        //model,  Cmd.ofMsg (StatusMsg_Set "Synced url to browser not implmented yet")
        (*
        match TaskState.cuaMode model.taskState, TaskState.chatMode model.taskState with
        | CUA_Init, CM_Init -> browserPostUrl model; model, Cmd.none
        | _, CM_Voice _ -> browserPostUrl model; model, Cmd.none
        | CUA_Loop, CM_Text _ -> model, Cmd.ofMsg (StatusMsg_Set "Cannot sync url to browser in current state")
        | _,_ -> model,Cmd.none
        *)

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
        OpTask.serialize strw opTask
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
                let t =  OpTask.deserialize str
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


    let updateTask model (instr:OpTask) =
        let task =
            {model.opTask with
                target=instr.target
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
                Cmd.ofMsg (SyncUrlToBrowser false)
                updtdMsg
            ]
            model, Cmd.batch cmds
        else
            model,updtdMsg

    let setInstructions model txt =
        let isDirty = txt <> model.opTask.textModeInstructions
        {model with opTask=OpTask.setTextPrompt txt model.opTask}, if isDirty then  Cmd.ofMsg OpTask_MarkDirty else Cmd.none

    let clearAll model =
         {model with flow = model.flow.Terminate(); opTask=OpTask.empty; action=""},
         Cmd.batch [
            Cmd.ofMsg (StatusMsg_Set "cleared")
            Cmd.ofMsg OpTask_ClearDirty
         ]

    let startBrowser model =
        model, Cmd.batch [
            Cmd.ofMsg (StatusMsg_Set "Staring browser..." );
            Cmd.OfAsync.either PlaywrightDriver.launchExternal () Browser_Connected Error]

    let terminateFlow model = 
        let voiceAsst = 
            model.voiceAsst
            |> Option.map RTOpenAI.Api.Connection.close
            |> Option.map RTOpenAI.Api.Connection.create
        {model with flow = model.flow.Terminate(); voiceAsst = voiceAsst},Cmd.none

    let startVoiceFlow model conn taskState = 
        let voicePrompt = 
            checkEmpty model.opTask.voiceAsstInstructions
            |> Option.orElse (Some Prompts.``starting voice prompt``)
            |> Option.map (fun t -> 
                t, Prompts.kernelArgs
                    [
                        Vars.taskInstructions, model.opTask.textModeInstructions
                        Vars.startUrl, model.opTask.target.TargetString()
                    ])
            |> Option.map (fun (t,args) -> Prompts.renderPrompt t args)
        let taskState = {taskState with toolDefs = taskState.toolDefs @ FlUtils.makeFunctionTools<Functions.FsOpVoice>()}
        let flow = TaskFlowInteractive.create taskState (Some conn) voicePrompt
        let model = {model with flow = {Flow.Default with state=FL_Flow {|flow=flow|}}}            
        async {
            do! Async.Sleep 100
            flow.Post TaskFlowInteractive.TFi_Prime
        } 
        |> Async.Start
        model,Cmd.none

    let configVoice driver bus  (b:IKernelBuilder) =
        let funcs = TaskFlowInteractive.createVoiceFunctions driver bus
        let voice = Functions.FsOpVoice()
        voice.SetFunctions(funcs)
        b.Plugins.AddFromObject(voice) |> ignore


    let startTextFlow model taskState  =
        let flow = TaskFlowInteractive.create taskState None None
        let model = {model with flow = {Flow.Default with state=FL_Flow {|flow=flow|}}}            
        async {
            do! Async.Sleep 100
            flow.Post TaskFlowInteractive.TFi_Prime
        } 
        |> Async.Start
        model,Cmd.none

    let startFlow (model:Model) =
        let taskState = lazy(
            let driver = PlaywrightDriver.create()
            let bus = WBus.Create<_,_> (Flow_Msg>>model.post)
            let kernel = OPlan.defaultKernel Map.empty (Some (configVoice driver.driver bus ))
            let tools = (FlUtils.makeFunctionTools<Functions.FsOpMemory>() @ FlUtils.makeFunctionTools<Functions.FsOpNavigator>()) 
            FsOpCore.TaskState.Create<_,_>  //initial task state
                        model.opTask.id
                        (model.opTask.target.TargetString())
                        bus
                        driver.driver
                        model.opTask.textModeInstructions
                        (checkEmpty model.opTask.reasonerInstructions |> Option.orElse (Some Prompts.``reasoner prompt for cua guidance``))
                        kernel
                        tools)
        match model.voiceAsst, checkEmpty model.opTask.textModeInstructions, OpTask.isTargetNotEmpty model.opTask.target with
        | Some conn, _, _ -> startVoiceFlow model conn taskState.Value
        | None, Some instr, true -> startTextFlow model taskState.Value
        | _, None, _ -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start task, no text-mode instructions given")
        | _,_,false    -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start task in text mode, no start URL provided")

(*
        match checkEmpty model.opTask.textModeInstructions, OpTask.isEmptyTarget model.opTask.target with 
        | Some instr, false -> 
            let driver = PlaywrightDriver.create()
            let kernel = OPlan.defaultKernel Map.empty (Some configVoice)
            let bus = WBus.Create<_,_> (Flow_Msg>>model.post)
            let tools = (FlUtils.makeFunctionTools<Functions.FsOpMemory>() @ FlUtils.makeFunctionTools<Functions.FsOpNavigator>()) 
            let tools = if model.voiceAsst.IsSome then FlUtils.makeFunctionTools<Functions.FsOpVoice>() @ tools else tools
            let t0 = FsOpCore.TaskState.Create<_,_>  //initial task state
                        model.opTask.id
                        (model.opTask.target.TargetString())
                        bus
                        driver.driver
                        model.opTask.textModeInstructions
                        (checkEmpty model.opTask.reasonerInstructions |> Option.orElse (Some Prompts.``reasoner prompt for cua guidance``))
                        kernel
                        tools
            let voicePrompt = 
                match model.voiceAsst with 
                | Some _ -> 
                    checkEmpty model.opTask.voiceAsstInstructions
                    |> Option.orElse (Some Prompts.``starting voice prompt``)
                    |> Option.map (fun t -> 
                        t, Prompts.kernelArgs
                            [
                                Vars.taskInstructions, model.opTask.textModeInstructions
                                Vars.startUrl, model.opTask.target.TargetString()
                            ])
                    |> Option.map (fun (t,args) -> Prompts.renderPrompt t args)
                | None -> None
            let flow = TaskFlowInteractive.create t0 model.voiceAsst voicePrompt
            let model = {model with flow = {Flow.Default with state=FL_Flow {|flow=flow|}}}            
            async {
                do! Async.Sleep 100
                flow.Post TaskFlowInteractive.TFi_Prime
            } 
            |> Async.Start
            model, Cmd.ofMsg (StatusMsg_Set "Started flow")
        | None,_ -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start flow, no instructions given")
        | _,true -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start flow, target is empty")
*)

    let voiceToggle model = 
        let voiceAsst = 
            match model.voiceAsst with 
            | Some v -> RTOpenAI.Api.Connection.close v; None
            | None   -> Some (RTOpenAI.Api.Connection.create())
        {model with voiceAsst = voiceAsst}, Cmd.none

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | OpTask_SetTextInstructions txt -> setInstructions model txt
            | OpTask_Update instr -> updateTask model instr
            | OpTask_MarkDirty -> let m = {model with isDirty = true} in setTitle win m; m, Cmd.none
            | OpTask_ClearDirty -> let m = {model with isDirty = false} in setTitle win m; m, Cmd.none
            | OpTask_SetTarget txt -> setTarget model txt
            //| OpTask_Load when (TaskState.cuaMode model.taskState).IsCUA_Init -> model, Cmd.OfAsync.either loadTask (win,model) OpTask_Loaded Error
            | OpTask_Load -> model,Cmd.none
            | OpTask_Loaded (Some instr) -> {model with opTask=instr; flow=Flow.Default}, Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg (SyncUrlToBrowser false)]
            | OpTask_Loaded None -> model, Cmd.none
            | OpTask_LoadSample sample -> model, Cmd.OfAsync.either checkLoadSample (win,model,sample) OpTask_Loaded Error
            | OpTask_Save -> model, Cmd.OfAsync.either saveTask (win,model.opTask) OpTask_Saved Error
            | OpTask_SaveAs -> model, Cmd.OfAsync.either saveTaskAs (win,model.opTask) OpTask_Saved Error
            | OpTask_Saved (Some t) -> {model with opTask=t},Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg (StatusMsg_Set $"saved {t.id}")]
            | OpTask_Saved None -> model, Cmd.none
            | OpTask_Clear -> clearAll model

            | ToggleVoiceMode -> voiceToggle model

            | SyncUrlToBrowser starBrowser -> syncUrl starBrowser model

            | Action_Set txt -> {model with action=txt}, Cmd.ofMsg (Action_Flash true)
            | Action_Flash isOn -> {model with isFlashing = isOn}, if isOn then Cmd.OfAsync.perform delayFlash (not isOn) Action_Flash else Cmd.none

            | Log_Append txt -> {model with log = (txt:: model.log) |> List.truncate 10}, Cmd.none
            | Log_Clear -> {model with log = []}, Cmd.none

            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear
            | Error exn -> Log.exn(exn,""); model, Cmd.ofMsg (Abort (Some exn,""))
            //| Abort (ex,msg) -> abort model ex msg
            | TestSomething -> testSomething model
            | Nop _ -> model, Cmd.none

            | Chat_CUATurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Chat_UpdateQuestion txt -> {model with flow = model.flow.setQuestion txt}, Cmd.none
            //| Chat_Append msg -> {model with taskState = TaskState.appendChatMsg msg model.taskState}, Cmd.none
            //| Chat_HandleTurnEnd -> handleTurnEnd model
            //| Chat_Resume ->  resumeTextCuaLoop model
            //| Chat_GotSummary_Cua (id,cntnt) -> reportProgress model (id,cntnt,true)
            //| Chat_GotSummary_Alt (id,cntnt) -> reportProgress model (id,cntnt,false)

            | Flow_StartStop when model.flow.isRunning() -> terminateFlow model
            | Flow_StartStop                             -> startFlow model
            | Flow_StopAndSummarize -> {model with flow = model.flow.stopAndSummarize()},Cmd.none
            | Flow_Resume -> {model with flow = model.flow.resume()},Cmd.none
            | Flow_Terminate -> terminateFlow model

            //handle messages emitted by a running flow
            | Flow_Msg (TaskFlowInteractive.TFo_Action action) -> model, Cmd.ofMsg (Action_Set action)
            | Flow_Msg (TaskFlowInteractive.TFo_Paused msgs)   -> {model with flow = model.flow.pause().setChatMsgs msgs}, Cmd.none
            | Flow_Msg (TaskFlowInteractive.TFo_ChatUpdated msgs) -> {model with flow = model.flow.setChatMsgs msgs}, Cmd.none
            | Flow_Msg (TaskFlowInteractive.TFo_Error e) -> model, [(StatusMsg_Set (string e)); Flow_Terminate] |> List.map Cmd.ofMsg |> Cmd.batch
            | Flow_Msg (TaskFlowInteractive.TFo_Done msgs) -> {model with flow = model.flow.setChatMsgs msgs}, Cmd.ofMsg Flow_Terminate
            | Flow_Msg (TaskFlowInteractive.TFo_Log s) -> model, Cmd.ofMsg (Log_Append s)
            | Flow_Msg (TaskFlowInteractive.TFo_Usage u) -> OPlan.printTaskUsage u; model,Cmd.none

            //| TextChat_StartStopTask -> startStopTextChat model
            //| VoiceChat_StartStop -> startStopVoiceChat model
            //| VoiceChat_RunInstructions (instructions,ev) -> startOrResumeVoiceCuaLoop model instructions ev
            //| _ -> model, Cmd.none

            | x -> model, Cmd.ofMsg (StatusMsg_Set $"{x} not handled")
        with ex ->
            printfn "%A" ex
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

