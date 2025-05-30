namespace FsOperator
open System
open FsOpCore

type TaskNode = {
    id : string
    description : string
    url : string option
    cuaInstructions : string
    reasonerInstructions : string option
    transition: string option
    children : TaskNode list
}
with 
    ///create a new empty task with globally unique id
    static member Create() = 
                    {
                        id = newId()
                        description = ""
                        url = None
                        cuaInstructions = ""
                        reasonerInstructions = None
                        transition = None
                        children = []
                    }

type Plan = {
    file : string option
    root : TaskNode
}

type TaskStatus = Init | Running | Done of string | Failed of string //done and failed string values may be use by transition


type TaskRunState = {
    taskId : string
    status : TaskStatus
    cuaContext :  FsResponses.InputOutputItem list list
    reasonerContext : FsResponses.InputOutputItem list list
}

type PlanExecution = {
    plan : Plan
    execution : TaskStatus list
}

module TaskNode =
    let internal checkId (id:string) = if String.IsNullOrWhiteSpace id then failwith $"invalid task id '{id}'"

    let internal check id1 id2 = checkId id1; checkId id2; if id1 = id1 then failwith $"id not unique '{id1}'"

    let internal checkIds (ids:string list) = 
        ids |> List.iter checkId
        let dups = ids |> List.countBy id |> List.filter (fun (_,c) -> c > 1)
        if dups.Length > 0 then failwithf $"duplicated ids {dups}"

    let rec ensureUnique child parent = 
        check child.id parent.id 
        parent.children |> List.iter (ensureUnique child)        

    let addChild child parent = 
        ensureUnique child parent
        {parent with children = child::parent.children}

    let rec remove id parent = {parent with children = parent.children |> List.filter (fun c -> c.id <> id)}

    (*
    *)
    let allIds root = 
        let rec loop acc p =
            let acc = p.id::acc
            match p.children  with 
            | [] -> List.rev acc
            | x::rest -> 
                let acc = x.id::acc 
                let acc = (acc,x.children) ||> List.fold loop
                (acc,rest) ||> List.fold loop
        loop [] root

    ///// Detects cycles in the TaskNode tree. Throws an exception if a cycle is found.
    //let detectCycle (root: TaskNode) =
    //    let rec dfs visited path node =
    //        if Set.contains node.id path then
    //            failwith $"Cycle detected: TaskNode with id '{node.id}' is revisited in the path"
    //        elif Set.contains node.id visited then
    //            visited // already fully processed
    //        else
    //            let path = Set.add node.id path
    //            let visited =
    //                node.children
    //                |> List.fold (dfs visited path) // recursively check children
    //            Set.add node.id visited
    //    ignore (dfs Set.empty Set.empty root)


module Plan =
    let validate plan = ()

