namespace FsOpCore
open Microsoft.SemanticKernel
open System.Threading
open Microsoft.Extensions.DependencyInjection
open System.Collections.Generic

///represents the target computer environment (browser url or windows exe) for a task
type OTaskTarget =
    | OProcess of string*string option
    | OLink of string
    with member this.TargetString() =
            match this with
            | OProcess (a,b) -> $"{a} {b}"
            | OLink s -> s

///definition of a single unit of work in a plan
type OTask = {
    id          : string
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Function list
    allowedSec  : int
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
                allowedSec = 60*10
            }

///Represents a 'choice' task node. One task of the available task is to be selected.
and Choose = {
    ///<summary>
    ///Prompt template to decide which one of the available tasks to run next.<br />
    ///Must be a SK prompt template with the following variable slots:<br />
    /// - {{taskDescriptions}} - will be used to supply the subtask descriptions<br />
    /// - {{context}} - will be filled with message history from previous task
    ///</summary>
    transitionPrompt : string

    nodes     : ONode list

    ///if the Choose node is a child of another Choose node
    ///then this description is used in the transition decision
    description:string option
}
with static member Default = {nodes=[]; transitionPrompt=""; description=None}

///represents a sequence of task nodes that are to be completed in order
and Seq = {
    nodes : ONode list

    ///if the All node is a child of a Choose node
    ///then this description is used in the transition decision
    description:string option
}
with static member Default = {nodes=[]; description=None}

///task node tree structure
and [<RequireQualifiedAccess; ReferenceEquality >] ONode =
    | Leaf of OTask      //Leaf node containing the task
    | Choose of Choose  //execute one of many sub nodes - based on LLM decision involving transition prompt
    | Seq of Seq        //execute all sub nodes in sequence
    with
        member this.allTasks() =
            let rec loop (visited:HashSet<ONode>,acc:OTask list) (p:ONode) = //need to protect against possible cycles
                if visited.Contains p then
                    (visited,acc)
                else
                    visited.Add p |> ignore
                    match p with
                    | ONode.Choose c -> ((visited,acc),c.nodes) ||> List.fold loop                                        
                    | ONode.Seq s    -> ((visited,acc),s.nodes) ||> List.fold loop
                    | ONode.Leaf t   -> (visited,t::acc)
            loop (HashSet(),[]) this |> snd

        member this.tasks() =
            match this with
            | ONode.Choose c -> c.nodes |> List.choose (function ONode.Leaf t -> Some t | _ -> None)
            | ONode.Seq s    -> s.nodes |> List.choose (function ONode.Leaf t -> Some t | _ -> None)
            | ONode.Leaf t   -> [t]

module ONode =
    ///Add a new node n
    ///under the parent p
    let addNode (parent:ONode) (n:ONode) (root:ONode) =
        let rec loop (visited:HashSet<ONode>) (c:ONode) =
            if visited.Contains c then 
                c
            else 
                visited.Add c |> ignore
                if parent = c then
                    match c with
                    | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes @ [n]}
                    | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes @ [n]}
                    | ONode.Leaf l -> failwith "a leaf node cannot be a parent"
                else
                    match c with
                    | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map (loop visited)}
                    | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map (loop visited)}
                    | t -> t
        loop (HashSet()) root

    ///Delete node under root. Fails if node does not exist. Return None if root is deleted.
    let deleteNode (n:ONode) (root:ONode) =
        let rec loop (visited:HashSet<_>,p:ONode option) (c:ONode) =
            if visited.Contains c 
                then Some c
            else 
                visited.Add c |> ignore
                match c=n with
                | true      -> None
                | false     -> match c with
                               | ONode.Seq s -> Some(ONode.Seq {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))})
                               | ONode.Choose s -> Some(ONode.Choose {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))})
                               | _ -> failwith "not expected"
        loop (HashSet(),None) root

    type Dir = Up | Down

    let private move dir (n:ONode) (ns:ONode list) =
        let i = ns |> List.tryFindIndex (fun n' -> n' = n)
        match i with
        | None -> ns
        | Some i ->
            match dir with
            | Up when i <> 0              -> ns |> List.removeAt i |> List.insertAt (i-1) n
            | Up                          -> ns
            | Down when i < ns.Length - 1 -> ns |> List.removeAt i |> List.insertAt (i+1) n
            | Down                        -> ns

    let moveNode dir (n:ONode) (parent:ONode) =
        match parent with
        | ONode.Seq s -> ONode.Seq {s with nodes = move dir n s.nodes}
        | ONode.Choose s -> ONode.Choose {s with nodes = move dir n s.nodes}
        | n -> n

    let rec duplicate (n:ONode) =
        match n with
        | ONode.Leaf t -> ONode.Leaf t
        | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map duplicate}
        | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map duplicate}

    let convertToSeq (n:ONode) (root:ONode) =
        let rec loop (c:ONode) =
            if c = n then
                match c with
                | ONode.Leaf t as n -> ONode.Seq {nodes=[n]; description=None}
                | ONode.Seq _ -> n
                | ONode.Choose c -> ONode.Seq {nodes=c.nodes; description=c.description}
            else
                match c with
                | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map loop}
                | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map loop}
                | t -> t
        loop root

    let convertToChoose (n:ONode) (root:ONode) =
        let rec loop (c:ONode) =
            if c = n then
                match c with
                | ONode.Leaf t as n -> ONode.Choose {nodes=[n]; description=None; transitionPrompt=""}
                | ONode.Seq s -> ONode.Choose {nodes=s.nodes; description=s.description; transitionPrompt=""}
                | ONode.Choose c as x -> x
            else
                match c with
                | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map loop}
                | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map loop}
                | t -> t
        loop root

    let replaceParent (n:ONode) (root:ONode) =
        let rec loop (gp:ONode option) (p:ONode option) (c:ONode) =
            match gp, p, c=n with
            | None, None, true -> Choice1Of2(c) //already root
            | None, Some _, true -> Choice1Of2(c) //just under root so replace root
            | Some gp, Some p, true -> Choice1Of2 c
            | _, p, false -> //recurse down
                match c with
                | ONode.Seq s as pn    -> let ns = s.nodes |> List.map (loop p (Some pn))
                                          let rep = ns |> List.tryPick(function Choice1Of2 n -> Some n | _ -> None)
                                          match rep with 
                                          | Some n -> Choice2Of2 n  //this node is parent, it was replaced by a child
                                          | _      -> let n = ONode.Seq {s with nodes = ns |> List.choose(function Choice2Of2 n -> Some n | _ -> None)}
                                                      Choice2Of2 n
                | ONode.Choose s as pn -> let ns = s.nodes |> List.map (loop p (Some pn))
                                          let rep = ns |> List.tryPick(function Choice1Of2 n -> Some n | _ -> None)
                                          match rep with 
                                          | Some n -> Choice2Of2 n  //this node is parent it was replaced by a child
                                          | _      -> let n = ONode.Choose {s with nodes = ns |> List.choose(function Choice2Of2 n -> Some n | _ -> None)}
                                                      Choice2Of2 n
                | _ -> failwith "not expected"
            | _,_,_ -> failwith "not expected"
        match loop None None root with 
        | Choice1Of2 n | Choice2Of2 n -> n

    ///All parent child relations
    let allEdges (root:ONode) =
        let rec loop (visited:HashSet<ONode>,acc:(ONode*ONode) list) (p:ONode) =
            if visited.Contains p then
                (visited,acc)
            else
                visited.Add p |> ignore
                match p with
                | ONode.Choose c -> let acc = acc @ (c.nodes |> List.map (fun x -> (p,x)))
                                    ((visited,acc),c.nodes) ||> List.fold loop
                | ONode.Seq s    -> let acc = acc @ (s.nodes |> List.map (fun x -> (p,x)))
                                    ((visited,acc),s.nodes) ||> List.fold loop
                | ONode.Leaf l   -> (visited,acc)
        loop (HashSet(),[]) root |> snd


///A collection of one or more tasks organized in a tree
type OPlan = {
    description : string
    root  : ONode
}
    with
        static member Default = {
                        description = ""
                        root = ONode.Seq {nodes=[]; description=None}
                    }

///run time state required to run a task
type OTaskRun = {
    task     : OTask
    driver   : IUIDriver
    messages : ChatMsg list
    usage    : Map<string,FsResponses.Usage>
}
with
    static member Create task driver =
                    {
                        task = task
                        driver = driver
                        messages = []
                        usage = Map.empty
                    }

    ///Configure the kernel so the functions run in the context of this task
    member this.HookFunctions(kernel:Kernel) =
        let nav = kernel.Services.GetService<Functions.FsOpNavigator>()
        if nav = Unchecked.defaultof<_> then
            failwith "FsOpNavigator service not found in kernel"
        nav.SetDriver  this.driver
        nav.SetStartUrl (this.task.target.TargetString())

///runtime state required to run a plan
type OPlanRun = {
    plan : OPlan
    kernel : Kernel
    completedTasks : OTaskRun list
    currentTask : OTaskRun option
}
with
    static member Create plan kernel =
                    {
                        plan = plan
                        kernel = kernel
                        completedTasks = []
                        currentTask = None
                    }

module OPlan =
    ///minimal 2-task sample plan
    let sample() =
        let ln =
            { OTask.Create() with
                target = OLink "https://www.linkedin.com"
                description = "find people who post about generative ai"
                tools = FlUtils.makeFunctionTools<Functions.FsOpMemory>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """find individuals who have original posts
related to generative AI and record their linkedin names and profile links.
Use the memory_save function to record this data as you find it.
Make sure to collect at least 5 names."""
                }
        let tw =
            { OTask.Create() with
                target = OLink "https://www.twitter.com"
                tools = FlUtils.makeFunctionTools<Functions.FsOpMemory>()
                description = "retrieve linkedIn people info from memory and get twitter handles"
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """Get the list of names and linked-in profile links from memory.
Search each name on twitter and obtain their twitter handle.
Use memory_save function to save each person's linked-in and twitter data into memory.
"""
            }
        let plan =
            { OPlan.Default with
                description = "take linkedin people and find their twitter handle"
                root = ONode.Seq {nodes= [ONode.Leaf ln; ONode.Leaf tw]; description=None}
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
        | ONode.Leaf t -> if doneSet.Contains t.id then Choice1Of3 () else Choice2Of3 t
        | ONode.Choose {nodes=ts} as cts ->
            let subIds = cts.allTasks() |> List.map _.id |> set
            let subsDone = Set.intersect doneSet subIds
            if subsDone.Count = 0 then
                Choice3Of3 cts //none of the child tasks are yet done so need to make a transition here
            else
                ts
                |> List.map (findNext doneSet)
                |> List.tryPick (function Choice2Of3 t as c -> Some c | Choice3Of3 _ as c -> Some c | _ -> None)
                |> Option.defaultValue (Choice1Of3 ())
        | ONode.Seq {nodes=ts} ->
            ts
            |> List.map (findNext doneSet)
            |> List.tryPick (function Choice2Of3 t as c -> Some c | Choice3Of3 _ as c -> Some c | _ -> None)
            |> Option.defaultValue (Choice1Of3 ())

    ///transition to the next task in the play
    let transition (c:Choose) (planRun:OPlanRun) = async {
        //TODO
        return None
    }

    let rec transitionToNext (planRun:OPlanRun)  = async {
        let doneTasks = match planRun.currentTask with | Some t -> t::planRun.completedTasks | _ -> planRun.completedTasks
        let doneSet = doneTasks |> List.map (fun tr -> tr.task.id) |> set
        match findNext doneSet planRun.plan.root with
        | Choice1Of3 _                 -> return None
        | Choice2Of3 t                 -> return Some t
        | Choice3Of3 (ONode.Choose c)  -> return! transition c planRun
        | x                            -> return failwith $"unexpected response in transitionToNext '{x}'"
    }

    let appendTask (tr:OTaskRun option) ts =  tr |> Option.map (fun t -> t::ts) |> Option.defaultValue ts

    let startTimer (n:int) (f:IFlow<_>) =
        async {
            do! Async.Sleep (n * 1000)
            f.Post TaskFlow.TFi_EndAndReport
        }
        |> Async.Start

    let reduceUsage (a:FsResponses.Usage) (b:FsResponses.Usage) =
        {a with
            input_tokens = a.input_tokens + b.input_tokens
            output_tokens = a.output_tokens + b.output_tokens
            total_tokens = a.total_tokens + b.total_tokens
        }

    let sumUsages (us:Map<string,FsResponses.Usage list>) =
        us
        |> Map.map (fun k vs ->
            vs
            |> List.reduce reduceUsage)

    let collectUsages (uss:Map<string,FsResponses.Usage> list) =
        uss
        |> List.collect Map.toList
        |> List.groupBy fst
        |> List.map (fun (k,xs) -> k, List.map snd xs)
        |> Map.ofList

    let printTaskUsage (us:Map<string,FsResponses.Usage list>) =
        sumUsages us
        |> Map.iter (fun m u -> printfn $"{m} inp:{u.input_tokens}, out:{u.output_tokens}, tot:{u.total_tokens}")

    ///Runs the current task set in planRun
    let runCurrentTask (planRun:OPlanRun) = async{
        match planRun.currentTask with
        | None -> return failwith $"no task to run"
        | Some ot ->
            use h = new ManualResetEvent(false)
            let completedTask = ref None
            let driver = (PlaywrightDriver.create().driver)
            let post = fun p ->
                match p with
                | TaskFlow.TFo_Done t -> completedTask.Value <- Some t; h.Set() |> ignore
                | TaskFlow.TFo_Error e -> printfn "%A" e;  h.Set() |> ignore
                | TaskFlow.TFo_Action a -> printfn "%A" a
                | TaskFlow.TFo_Paused msgs -> printfn "%A" msgs
                | TaskFlow.TFo_Usage us -> printTaskUsage us
            let bus = WBus.Create<_,_> post
            let t0 = TaskState.Create<_,_>  //initial task state
                        ot.task.id
                        (ot.task.target.TargetString())
                        bus
                        driver
                        ot.task.cua.Value
                        ot.task.reasoner
                        planRun.kernel
                        ot.task.tools
            match ot.task.target with
            | OLink url -> do! driver.start url
            | OProcess (a,b) -> ()
            let flow = TaskFlow.create t0
            flow.Post TaskFlow.TFi_Start
            startTimer ot.task.allowedSec flow //sends task terminate message when this timer expires
            let! r = Async.AwaitWaitHandle(h,ot.task.allowedSec * 1000 * 3) //max wait for task to finish in case its stuck
            match completedTask.Value with
            | Some t -> return {ot with messages = t.cuaMessages; usage = sumUsages t.usage}
            | None   -> return failwith "no output from step"
    }

    ///Single step the plan: Transition to next task and run it or end if no task.
    let step planRun = async {
        match! transitionToNext planRun with
        | None ->
            return
                {planRun with
                    currentTask = None
                    completedTasks = appendTask planRun.currentTask planRun.completedTasks}
        | Some t ->
                let tr = OTaskRun.Create t (PlaywrightDriver.create().driver)
                let planRun =
                        {planRun with
                            currentTask = Some tr
                            completedTasks = appendTask planRun.currentTask planRun.completedTasks
                         }
                let! tr' = runCurrentTask planRun
                return {planRun with currentTask = Some tr'}
    }

    ///Create a kernel with the required plugins and services for running tasks.
    ///Optionally supply a function to perform additional configuration.
    ///The initialMemory will be added to the memory plugin
    let defaultKernel (initialMemory:Map<string,string list>) (build:(IKernelBuilder->unit) option) =
        let b = Kernel.CreateBuilder()
        match build with Some build -> build b | _ -> ()
        let nav = Functions.FsOpNavigator()
        let mem = Functions.FsOpMemory()
        mem.SetMemory initialMemory
        b.Plugins.AddFromObject(mem) |> ignore
        b.Plugins.AddFromObject(nav) |> ignore
        b.Services.AddSingleton(nav) |> ignore
        b.Build()

    let rec run planRun = async {
        let! planRun = step planRun
        if planRun.currentTask.IsSome then
            return! run planRun
        else
            return planRun
    }
