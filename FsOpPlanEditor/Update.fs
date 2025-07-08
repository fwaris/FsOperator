namespace FsOpPlanEditor
open Avalonia.Input
open Avalonia.Threading
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks
open Elmish
open FsOpCore

module Update =
    let init p   =
        let model = {
            plan = FsOpCore.OPlan.Default
            tasks = p.root.allSubtasks()
            nodes = []
        }
        model, Cmd.ofMsg (Init p)

    let doDrag e = 
        async {
            let dragData = DataObject()
            dragData.Set(DataFormats.Text, "drag")

            let! result = Dispatcher.UIThread.InvokeAsync<DragDropEffects>
                            (fun _ -> DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy)) |> Async.AwaitTask

            return match result with 
                    | DragDropEffects.Copy -> "The text was copied"
                    | DragDropEffects.Link -> "The text was linked"
                    | DragDropEffects.None -> "The drag operation was canceled"
                    | _                    -> "unexpected"
        }

    let addNode model nid = 
        match model.tasks |> List.tryFind(fun n -> n.id = nid) with 
        | Some n -> {model with nodes = n::model.nodes |> List.distinct}
        | None -> model

    let update (win:HostWindow)  (tcs:TaskCompletionSource<OPlan option>) msg (model:Model) =
        try
            match msg with
            | BeginDrag e -> model, Cmd.OfAsync.perform doDrag e Dragged
            | Dragged s -> model,Cmd.none
            | Dropped s -> addNode model s, Cmd.none
            | Init p -> {model with plan = p},Cmd.none
            | Close -> tcs.SetResult(None); win.Close(); model,Cmd.none
            | Save  -> tcs.SetResult(Some model.plan); win.Close(); model,Cmd.none
            | EditTask t -> model,Cmd.none
            | AddTask -> {model with tasks = (OTask.Create())::model.tasks},Cmd.none
        with ex ->
            Log.exn(ex,"update")
            model,Cmd.none

            