namespace FsOpPlanEditor
open FsOpCore

type Model = {
    plan : OPlan
    tasks : OTask list
}

type Msg =
    | Close 
    | Save
    | EditTask
    | AddTask
    | Init of OPlan


