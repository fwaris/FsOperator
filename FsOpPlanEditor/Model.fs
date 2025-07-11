namespace FsOpPlanEditor
open FsOpCore
open Avalonia.Input

type Model = {
    plan : OPlan
    tasks : OTask list
    nodes : OTask list
    root : FsOpCore.ONode
}

type Msg =
    | Close
    | Save
    | EditTask of OTask
    | Init of OPlan
    | BeginDrag of PointerPressedEventArgs*OTask
    | Dragged of string
    | Dropped of OTask
    | ConvertToSequence of ONode
    | ReplaceParent of ONode
    | ConvertToChoose of ONode
    | DeleteNode of ONode
    | EditNode of ONode
    | AddTask of ONode


