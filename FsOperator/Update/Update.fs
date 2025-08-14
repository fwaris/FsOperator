namespace FsOperator
open System
open System.Threading.Channels
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts
open FsOpCore
open Microsoft.SemanticKernel
open FsOpCore.Interactive

module Update =

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
            mailbox = Subscriptions.mailbox
            log = []
            action = ""
            statusMsg = None,""
            isFlashing = false
            ui = ui
            driver = ui.driver
            flow = Flow.Default
            voiceAsst = None
            plan = Some (SpreadsheetToEmailPlan.create())

        }
        model,Cmd.none

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | OpTask_SetTextInstructions txt -> Tasks.setInstructions model txt
            | OpTask_Update instr -> Tasks.updateTask model instr
            | OpTask_MarkDirty -> let m = {model with isDirty = true} in Tasks.setTitle win m; m, Cmd.none
            | OpTask_ClearDirty -> let m = {model with isDirty = false} in Tasks.setTitle win m; m, Cmd.none
            | OpTask_SetTarget txt -> Tasks.setTarget model txt
            //| OpTask_Load when (TaskState.cuaMode model.taskState).IsCUA_Init -> model, Cmd.OfAsync.either loadTask (win,model) OpTask_Loaded Error
            | OpTask_Load -> model,Cmd.none
            | OpTask_Loaded (Some instr) -> {model with opTask=instr; flow=Flow.Default}, Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg (SyncUrlToBrowser false)]
            | OpTask_Loaded None -> model, Cmd.none
            | OpTask_LoadSample sample -> model, Cmd.OfAsync.either Tasks.checkLoadSample (win,model,sample) OpTask_Loaded Error
            | OpTask_Save -> model, Cmd.OfAsync.either Tasks.saveTask (win,model.opTask) OpTask_Saved Error
            | OpTask_SaveAs -> model, Cmd.OfAsync.either Tasks.saveTaskAs (win,model.opTask) OpTask_Saved Error
            | OpTask_Saved (Some t) -> {model with opTask=t},Cmd.batch [Cmd.ofMsg OpTask_ClearDirty; Cmd.ofMsg (StatusMsg_Set $"saved {t.id}")]
            | OpTask_Saved None -> model, Cmd.none
            | OpTask_Clear -> Tasks.clearAll model

            | ToggleVoiceMode -> Flows.voiceToggle model

            | SyncUrlToBrowser starBrowser -> Tasks.syncUrl starBrowser model

            | Action_Set txt -> {model with action=txt}, Cmd.ofMsg (Action_Flash true)
            | Action_Flash isOn -> {model with isFlashing = isOn}, if isOn then Cmd.OfAsync.perform Tasks.delayFlash (not isOn) Action_Flash else Cmd.none

            | Log_Append txt -> {model with log = (txt:: model.log) |> List.truncate 10}, Cmd.none
            | Log_Clear -> {model with log = []}, Cmd.none

            | StatusMsg_Clear dt -> (if Tasks.shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  Tasks.delayClearStatus t StatusMsg_Clear
            | Error exn -> Log.exn(exn,""); model, Cmd.ofMsg (Abort (Some exn,""))
            | Abort (ex,msg) -> model, Cmd.ofMsg Flow_Terminate
            | TestSomething -> testSomething model
            | Nop _ -> model, Cmd.none

            | Flow_UpdateQuestion txt -> {model with flow = model.flow.setQuestion txt}, Cmd.none
            | Flow_StartStop when model.flow.isRunning() -> Flows.terminateFlow model
            | Flow_StartStop                             -> Flows.startFlow model
            | Flow_StopAndSummarize -> {model with flow = model.flow.stopAndSummarize()},Cmd.none
            | Flow_Resume -> {model with flow = model.flow.resume()},Cmd.none
            | Flow_Terminate -> Flows.terminateFlow model

            //handle messages emitted by a running flow
            | Flow_Msg (APo_Action action) -> model, Cmd.ofMsg (Action_Set action)
            | Flow_Msg (APo_Paused msgs)   -> {model with flow = model.flow.pause().setChatMsgs msgs}, Cmd.none
            | Flow_Msg (APo_Updated msgs) -> {model with flow = model.flow.setChatMsgs msgs}, Cmd.none
            | Flow_Msg (APo_Error e) -> model, [(StatusMsg_Set (string e)); Flow_Terminate] |> List.map Cmd.ofMsg |> Cmd.batch
            | Flow_Msg (APo_Done t) -> {model with flow = model.flow.setChatMsgs t.cuaMessages}, Cmd.ofMsg Flow_Terminate
            | Flow_Msg (APo_Log s) -> model, Cmd.ofMsg (Log_Append s)
            | Flow_Msg (APo_Usage u) -> OPlan.printTaskUsage u; model,Cmd.none
           
            | Plan_Edit -> model, Cmd.OfTask.either Plans.editPlan (win,model) Plan_Set Error
            | Plan_Set p -> {model with plan = p},Cmd.none

//            | x -> model, Cmd.ofMsg (StatusMsg_Set $"{x} not handled")
        with ex ->
            printfn "%A" ex
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

