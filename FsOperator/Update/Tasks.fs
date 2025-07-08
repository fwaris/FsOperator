namespace FsOperator
open System
open FsOpCore
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts
open Avalonia.Threading

module Tasks = 

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
