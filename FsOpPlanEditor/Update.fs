namespace FsOpPlanEditor
open Elmish
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks
open FsOpCore

module Update =
    let init p   =
        let model = {
            plan = FsOpCore.OPlan.Default
            tasks = p.root.allSubtasks()
        }
        model, Cmd.ofMsg (Init p)

    let update (win:HostWindow)  (tcs:TaskCompletionSource<OPlan option>) msg (model:Model) =
        try
            match msg with
            | Init p -> {model with plan = p},Cmd.none
            | Close -> tcs.SetResult(None); win.Close(); model,Cmd.none
            | Save  -> tcs.SetResult(Some model.plan); win.Close(); model,Cmd.none
            | EditTask -> model,Cmd.none
            | AddTask -> {model with tasks = (OTask.Create())::model.tasks},Cmd.none
        with ex ->
            Log.exn(ex,"update")
            model,Cmd.none

            
