namespace FsOpPlanEditor
open Avalonia.Input
open Avalonia.Threading
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks
open Elmish
open FsOpCore
open AvaloniaGraphControl

module Msagl = 
    open Microsoft.Msagl.Drawing
    open System.Reflection

    let dedge = lazy(
        typeof<AvaloniaGraphControl.Edge>.GetField("DEdge",BindingFlags.NonPublic ||| BindingFlags.Instance))

    ///this only works after the graph is rendered
    let style (e:AvaloniaGraphControl.Edge) = 
        let be = box e       
        let de = try dedge.Value.GetValue(be) with _ -> null
        match de with 
        | null          -> e
        | :? Edge as de -> de.Attr.Color <- Color.Azure
                           e
        | _             -> e

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

    let graph (root:ONode) =
        let g = Graph()
        let e0 = Edge("",root)
        let edges = e0 :: (ONode.allEdges root |> List.map(fun (p,c) -> Edge(p,c)))
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

    let droppedNodeOn (model:Model) (droppedNode,anchorNode) = 
        {model with root = model.root |> ONode.moveNode droppedNode anchorNode}, Cmd.none

    let update (win:HostWindow)  (tcs:TaskCompletionSource<OPlan option>) msg (model:Model) =
        try
            match msg with
            | BeginDrag (e,t) -> model, Cmd.OfAsync.perform doDrag (e,t) Dragged
            | Dragged s -> model,Cmd.none
            | DroppedNodeOn (droppedNode,anchorNode) -> droppedNodeOn model (droppedNode,anchorNode)
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

