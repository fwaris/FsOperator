namespace FsOpPlanEditor
open FsOpCore
open Avalonia.Input

type Model = {
    plan : OPlan
    tasks : OTask list
    nodes : OTask list
}

type Msg =
    | Close 
    | Save
    | EditTask of OTask
    | AddTask
    | Init of OPlan
    | BeginDrag of PointerPressedEventArgs*OTask
    | Dragged of string
    | Dropped of OTask

