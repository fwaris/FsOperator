namespace FsOpPlanEditor
open FsOpCore

type Model = {
    plan : OPlan
}

type Msg =
    | Close 
    | Init of OPlan


