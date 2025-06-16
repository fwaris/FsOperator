namespace FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open FsResponses

type OPlanMemory() =
    let mutable map = Map.empty

    [<KernelFunction("save_memory")>]
    [<Description("Save a key-value pair for later retrieval")>]
    member this.save_memory(key:string, value:string) = map <- map |> Map.add key value

    [<KernelFunction("get_all_keys")>]
    [<Description("retrieve all keys in the memory store ")>]
    member this.get_all_keys() = map.Keys |> Seq.toList

    [<KernelFunction("get_memory")>]
    [<Description("retrieve a value for the given key")>]
    member this.get_memory(key:string) = map |> Map.tryFind key
        
type OTaskTarget = OProcess of string*string option | OLink of string

type OTask = {
    id          : string
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Tool list
}
    with 
        ///creates a new empty task with unique id assigned
        static member Create() =  {
                id = newId()
                target = OLink ""
                description = ""
                cua = None
                reasoner = None
                voice = None
                tools = []
            }

type OTaskTransition = 
    {
        ///<summary>
        ///Prompt template to decide which one of the available tasks to run next.<br />
        ///Must be a SK prompt template with the following variable slots:<br /> 
        /// - {{taskDescriptions}} - will be used to supply the subtask descriptions<br />
        /// - {{context}} - will be filled with message history from previous task
        ///</summary>
        transitionPrompt : string 
        
        ///list of available tasks to choose from
        nodes            : ONode list

    }

and Choose = {
    transition:OTaskTransition

    ///if the Choose node is a child of another Choose node
    ///then this description is used in the transition decision
    description:string option
}

and All = {
    nodes : ONode list

    ///if the All node is a child of a Choose node
    ///then this description is used in the transition decision
    description:string option
}

and [<RequireQualifiedAccess>] ONode = 
    | One of OTask      //Leaf node containing the task
    | Choose of Choose  //execute one of many sub nodes - based on LLM decision involving transition prompt
    | All of All        //execute all sub nodes in sequence
    with 
        member this.allSubtasks() =
            let rec loop acc n = 
                match n with
                | One t -> (t::acc) 
                | All all -> (acc,all.nodes) ||> List.fold loop
                | Choose {transition={nodes=ns}} -> (acc,ns) ||> List.fold loop
            loop [] this
    

type OPlan = {
    description : string
    root  : ONode
}
    with 
        static member Default = {
                        description = ""
                        root = ONode.All {nodes=[]; description=None}
                    }

type OTaskRun = {
    task     : OTask
    driver   : IUIDriver
    messages : ChatMsg list    
}

type OPlanRun = {
    plan : OPlan
    kernel : Kernel
    completedTasks : OTaskRun list
    currentTask : OTaskRun option
}

module OPlan =

    let sample = 
        let ln = 
            { OTask.Create() with
                target = OLink "https://www.linkedin.com"
                description = "find people who post about generative ai"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                cua = Some """find individuals who have original posts
related to generative AI and record there linkedin names and profile links.
Use the save_memory function to record each name as you find it.
"""
                }
        let tw = 
            { OTask.Create() with
                target = OLink "https://www.twitter.com"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                description = "retrieve linkedIn people info from memory and get twitter handles"
                cua = Some """
list of names and linked in profile links. Search each name on twitter and obtain their
twitter handle. 
Use save_memory function to save each person's linked-in and twitter data
    """        
            }
        let plan = 
            { OPlan.Default with
                description = "take linkedin people and find their twitter handle"
                root = ONode.All {nodes= [ONode.One ln; ONode.One tw]; description=None}
            }
        plan

    /// <summary>
    /// Find the next task or transition to execute for the given OTaskNode.<br />
    ///Choices:<br />
    /// - 1of3 - nothing more to do<br />
    /// - 2of3 - execute the OTask<br />
    /// - 3of3 - transition to a Choose child 
    /// </summary>
    let rec findNext (doneSet:Set<string>) = function 
        | ONode.One t -> if doneSet.Contains t.id then Choice1Of3 () else Choice2Of3 t
        | ONode.Choose {transition={nodes=ts}} as cts -> 
            let subIds = cts.allSubtasks() |> List.map _.id |> set
            let subsDone = Set.intersect doneSet subIds
            if subsDone.Count = 0 then 
                Choice3Of3 cts //none of the child tasks are yet done so need to make a transition here
            else
                ts 
                |> List.map (findNext doneSet)
                |> List.tryPick (function Choice2Of3 t as c -> Some c | Choice3Of3 _ as c -> Some c | _ -> None)
                |> Option.defaultValue (Choice1Of3 ())                
        | ONode.All {nodes=ts} ->
            ts 
            |> List.map (findNext doneSet)
            |> List.tryPick (function Choice2Of3 t as c -> Some c | Choice3Of3 _ as c -> Some c | _ -> None)
            |> Option.defaultValue (Choice1Of3 ())                


    let transition (txn:OTaskTransition) (planRun:OPlanRun) = async {
        //TODO
        return None
    }
    
    let rec transitionToNext (planRun:OPlanRun)  = async {
        let doneSet = planRun.completedTasks |> List.map (fun tr -> tr.task.id) |> set
        match findNext doneSet planRun.plan.root with 
        | Choice1Of3 _                 -> return None
        | Choice2Of3 t                 -> return Some t
        | Choice3Of3 (ONode.Choose c)  -> return! transition c.transition planRun
        | x                            -> return failwith $"unexpected response in transitionToNext '{x}'"
    }


    let appendTask (tr:OTaskRun option) ts =  tr |> Option.map (fun t -> t::ts) |> Option.defaultValue ts

    let runCurrentTask (planRun:OPlanRun) = async{
        //TODO
        return planRun
    }
   
    let step planRun = async {
        match! transitionToNext planRun with
        | None -> 
            return
                {planRun with 
                    currentTask = None
                    completedTasks = appendTask planRun.currentTask planRun.completedTasks}
        | Some t ->
                let tr = {task = t; driver = PlaywrightDriver.create().driver; messages=[]}
                let planRun = 
                        {planRun with 
                            currentTask = Some tr
                            completedTasks = appendTask planRun.currentTask planRun.completedTasks
                         }
                return! runCurrentTask planRun 
    }

