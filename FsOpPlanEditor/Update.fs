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
            root = p.root
            prevRoot = None
            undoStack = []
            redoStack = []
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

    let stack (model:Model) =       
        match model.prevRoot with 
        | Some r -> {model with prevRoot=None; undoStack=r::model.undoStack; redoStack=[]}
        | None -> model
        
    let updateRoot model root = {model with root=root; prevRoot=Some model.root} |> stack

    let droppedNodeOn (model:Model) (droppedNode,anchorNode) = 
        let root = model.root |> ONode.moveNode droppedNode anchorNode
        updateRoot model root, Cmd.none

    let undo (model:Model) =
        let model =
            match model.undoStack with 
            | [] -> model
            | x::rest -> {model with root=x; undoStack=rest; redoStack=model.root::model.redoStack}
        model,Cmd.none

    let redo (model:Model) =
        let model =
            match model.redoStack with 
            | [] -> model
            | x::rest -> {model with root=x; undoStack=model.root::model.undoStack; redoStack=rest}
        model,Cmd.none


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

            | ConvertToChoose n -> updateRoot model (ONode.convertToChoose n model.root), Cmd.none
            | ConvertToSequence n -> updateRoot model (ONode.convertToSeq n model.root), Cmd.none
            | ReplaceParent n -> updateRoot model (ONode.replaceParent n model.root), Cmd.none
            | DeleteNode n -> updateRoot model (ONode.deleteNode n model.root |> Option.defaultValue (ONode.Seq Seq.Default)), Cmd.none
            | EditNode n -> model, Cmd.none
            | AddTask n -> updateRoot model (addTask model.root n), Cmd.none
            | Undo -> undo model
            | Redo -> redo model
        with ex ->
            Log.exn(ex,"update")
            model,Cmd.none

