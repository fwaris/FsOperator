namespace FsOpPlanEditor
open FsOpCore
open Avalonia.Input

type Model = {
    plan : OPlan
    root : FsOpCore.ONode
    orientation : AvaloniaGraphControl.Graph.Orientations
    prevRoot : FsOpCore.ONode option
    undoStack : FsOpCore.ONode list
    redoStack : FsOpCore.ONode list
}

type Msg =
    | Close
    | Save
    | EditTask of OTask
    | Init of OPlan
    | BeginDrag of PointerPressedEventArgs*ONode
    | Dragged of string
    | DroppedNodeOn of ONode*ONode
    | ConvertToSequence of ONode
    | ReplaceParent of ONode
    | ConvertToChoose of ONode
    | DeleteNode of ONode
    | EditNode of ONode
    | AddTask of ONode
    | Redo
    | Undo
    | ToggleOrientation
