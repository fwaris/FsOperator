namespace FsOpPlanEditor
open Avalonia.Input
open Avalonia.Threading
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks
open Elmish
open FsOpCore
open AvaloniaGraphControl

module Update =
    open System.Collections.Generic
    let init p   =
        let model = {
            plan = FsOpCore.OPlan.Default
            tasks = p.root.allTasks()
            nodes = []
            root = p.root
        }
        model, Cmd.ofMsg (Init p)

    let edges (root:ONode) =
        let rec loop (visited:HashSet<ONode>,acc:Edge list) (p:ONode) =
            if visited.Contains p then
                (visited,acc)
            else
                visited.Add p |> ignore
                match p with
                | ONode.Choose c -> let acc = acc @ (c.nodes |> List.map (fun x -> Edge(p,x)))
                                    ((visited,acc),c.nodes) ||> List.fold loop
                | ONode.Seq s    -> let acc = acc @ (s.nodes |> List.map (fun x -> Edge(p,x)))
                                    ((visited,acc),s.nodes) ||> List.fold loop
                | ONode.Leaf l   -> (visited,acc)
        loop (HashSet(),[]) root

    let graph (_,edges) =
        let g = Graph()
        for e in edges do
            g.Edges.Add e
        g

    let doDrag (e,t) =
        async {
            let dragData = DataObject()
            dragData.Set(DataFormats.Text,t)

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

    let newTaskId (n:ONode) =
        let sts = n.allTasks() |> List.map _.id |> set
        let rec loop c =
            let id = $"task {c}"
            if sts.Contains id then
                loop (c+1)
            else
                id
        loop 1

    let addTask (root:ONode) (p:ONode) =
        let t = {OTask.Create() with id = newTaskId root}
        ONode.addNode p (ONode.Leaf t) root

    let update (win:HostWindow)  (tcs:TaskCompletionSource<OPlan option>) msg (model:Model) =
        try
            match msg with
            | BeginDrag (e,t) -> model, Cmd.OfAsync.perform doDrag (e,t) Dragged
            | Dragged s -> model,Cmd.none
            | Dropped t -> {model with nodes = (t::model.nodes) |> List.distinct}, Cmd.none
            | Init p -> {model with plan = p},Cmd.none
            | Close -> tcs.SetResult(None); win.Close(); model,Cmd.none
            | Save  -> tcs.SetResult(Some model.plan); win.Close(); model,Cmd.none
            | EditTask t -> model,Cmd.none

            | ConvertToChoose n -> {model with root = ONode.convertToChoose n model.root}, Cmd.none
            | ConvertToSequence n -> {model with root = ONode.convertToSeq n model.root}, Cmd.none
            | ReplaceParent n -> {model with root = ONode.replaceParent n model.root}, Cmd.none
            | DeleteNode n -> {model with root = ONode.deleteNode n model.root |> Option.defaultValue (ONode.Seq Seq.Default)}, Cmd.none
            | EditNode n -> model, Cmd.none
            | AddTask n -> {model with root = addTask model.root n}, Cmd.none


        with ex ->
            Log.exn(ex,"update")
            model,Cmd.none

