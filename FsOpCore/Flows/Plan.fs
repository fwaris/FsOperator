namespace FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.Threading
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open System.Text.Json.Serialization
open System.Collections.Concurrent

///represents the target computer environment (browser url or windows exe) for a task
type OTaskTarget = OProcess of string*string option | OLink of string

///definition of a single unit of work in a plan
type OTask = {
    id          : string
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Tool list
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

///Prompts to help transition to the next task after completion of the current task.
///Note the 'next' task could be one of the available task choices
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

///Represents a 'choice' task node. One task of the available task is to be selected.
and Choose = {
    transition:OTaskTransition

    ///if the Choose node is a child of another Choose node
    ///then this description is used in the transition decision
    description:string option
}

///represents a sequence of task nodes that are to be completed in order
and Seq = {
    nodes : ONode list

    ///if the All node is a child of a Choose node
    ///then this description is used in the transition decision
    description:string option
}

///task node tree structure
and [<RequireQualifiedAccess>] ONode =
    | Leaf of OTask      //Leaf node containing the task
    | Choose of Choose  //execute one of many sub nodes - based on LLM decision involving transition prompt
    | Seq of Seq        //execute all sub nodes in sequence
    with
        member this.allSubtasks() =
            let rec loop acc n =
                match n with
                | Leaf t -> (t::acc)
                | Seq all -> (acc,all.nodes) ||> List.fold loop
                | Choose {transition={nodes=ns}} -> (acc,ns) ||> List.fold loop
            loop [] this

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
}

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

///plugin that provides a navigation function
type Navigator() =
    let plan:Ref<OPlanRun> = ref Unchecked.defaultof<_>

    member this.PlanRef = plan

    [<KernelFunction("home")>]
    [<Description("Load initial task page")>]
    member this.home() =
        let comp = async {
            Log.info $"{nameof this.home}"
            if plan.Value <> Unchecked.defaultof<_> then 
                match plan.Value.currentTask with
                | Some t ->
                    match t.task.target with
                    | OLink url -> do! t.driver.start url
                    | _         -> ()
                | None -> ()
                return "home page loaded"
            else
                return "a home page url not found"
        }
        Async.StartAsTask comp

    [<KernelFunction("get_current_url")>]
    [<Description("Get the current URL of the browser")>]
    member this.get_current_url() =
        let comp = async {
            Log.info $"{nameof this.get_current_url}"
            let noUrl = "unable to get url"
            if plan.Value <> Unchecked.defaultof<_> then 
                match plan.Value.currentTask with
                | Some t ->
                    match! t.driver.url() with 
                    | Some url -> return url
                    | None     -> return noUrl
                | None -> return noUrl
            else
                return noUrl
        }
        Async.StartAsTask comp

///semantic kernel 'plugin' class that implements memory functions
type OPlanMemory() =
    let mutable bag = Map.empty
    static member statefile = lazy(homePath.Value @@ "memory.json")

    static member serOpts = lazy(
        let opts =JsonSerializerOptions()
        opts.WriteIndented <- true
        opts)

    member this.SetMemory(m) = bag <- m

    static member LoadState() =
        try
            if System.IO.File.Exists(OPlanMemory.statefile.Value) then
                use str = System.IO.File.OpenRead(OPlanMemory.statefile.Value)
                let map = System.Text.Json.JsonSerializer.Deserialize<Map<string,string list>>(str)
                let mem = new OPlanMemory()
                let bag = ConcurrentBag<string>()
                mem.SetMemory(map)
                mem
            else
                new OPlanMemory()
        with ex ->
            Log.exn(ex, nameof OPlanMemory.LoadState)
            new OPlanMemory()

    member this.Serialize<'t>(o:'t) = JsonSerializer.Serialize(o,options=OPlanMemory.serOpts.Value)

    static member private _SaveState(map:Map<string,string list>) =
        try
            use str = System.IO.File.Create OPlanMemory.statefile.Value
            JsonSerializer.Serialize(str,map, options=OPlanMemory.serOpts.Value)
        with ex ->
            Log.exn(ex,nameof OPlanMemory._SaveState)

    [<KernelFunction("memory_save")>]
    [<Description("Save a key-value pair for later retrieval")>]
    member this.memory_save(key:string, value:string) =
        Log.info $"{nameof this.memory_save}:{key} = {value}"
        lock bag (fun _ -> 
            bag <-
                bag 
                |> Map.tryFind key 
                |> Option.map (fun vs -> bag |> Map.add key (List.distinct (value::vs)))
                |> Option.defaultWith (fun _ -> bag |> Map.add key [value])
            OPlanMemory._SaveState(bag)
        )
        "saved"

    [<KernelFunction("memory_get_all")>]
    [<Description("Retrieve all key value pairs saved in memory")>]
    member this.memory_get_all() =
        Log.info (nameof this.memory_get_all)
        this.Serialize(bag)

    [<KernelFunction("memory_get_all_keys")>]
    [<Description("retrieve all keys in the memory store ")>]
    member this.memory_get_all_keys() =
        let ks = Map.keys bag |> Seq.toList
        Log.info $"{nameof this.memory_get_all_keys}: {ks}"
        this.Serialize(ks)

    [<KernelFunction("memory_get_value")>]
    [<Description("retrieve a value for the given key")>]
    member this.memory_get_value(key:string) =
        let v = bag |> Map.tryFind key
        Log.info $"{nameof this.memory_get_value} {key} = {v}"
        this.Serialize(v)

module OPlan =
    ///minimal 2-task sample plan
    let sample() =
        let ln =
            { OTask.Create() with
                target = OLink "https://www.linkedin.com"
                description = "find people who post about generative ai"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """find individuals who have original posts
related to generative AI and record their linkedin names and profile links.
Use the memory_save function to record this data as you find it.
Make sure to collect at least 5 names."""
                }
        let tw =
            { OTask.Create() with
                target = OLink "https://www.twitter.com"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
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
        | ONode.Seq {nodes=ts} ->
            ts
            |> List.map (findNext doneSet)
            |> List.tryPick (function Choice2Of3 t as c -> Some c | Choice3Of3 _ as c -> Some c | _ -> None)
            |> Option.defaultValue (Choice1Of3 ())

    ///transition to the next task in the play
    let transition (txn:OTaskTransition) (planRun:OPlanRun) = async {
        //TODO
        return None
    }

    let rec transitionToNext (planRun:OPlanRun)  = async {
        let doneTasks = match planRun.currentTask with | Some t -> t::planRun.completedTasks | _ -> planRun.completedTasks
        let doneSet = doneTasks |> List.map (fun tr -> tr.task.id) |> set
        match findNext doneSet planRun.plan.root with
        | Choice1Of3 _                 -> return None
        | Choice2Of3 t                 -> return Some t
        | Choice3Of3 (ONode.Choose c)  -> return! transition c.transition planRun
        | x                            -> return failwith $"unexpected response in transitionToNext '{x}'"
    }

    let appendTask (tr:OTaskRun option) ts =  tr |> Option.map (fun t -> t::ts) |> Option.defaultValue ts

    let startTimer (n:int) (f:IFlow<_>) =
        async {
            do! Async.Sleep (n * 1000)
            f.Post PlanFlow.TFi_EndAndReport
        }
        |> Async.Start

    ///Runs the current task set in planRun
    let runCurrentTask (planRun:OPlanRun) = async{
        match planRun.currentTask with
        | None -> return failwith $"no task to run"
        | Some ot ->
            use h = new ManualResetEvent(false)
            let completedTask = ref None
            let driver = (PlaywrightDriver.create().driver)
            let post = fun p ->
                printfn "%A" p
                match p with
                | PlanFlow.TFo_Done t -> completedTask.Value <- Some t; h.Set() |> ignore
                | PlanFlow.TFo_Error e -> printfn "%A" e;  h.Set() |> ignore
                | PlanFlow.TFo_Action a -> ()//printfn "%A" a
                | PlanFlow.TFo_Paused msgs -> printfn "%A" msgs
            let bus = WBus.Create<_,_> post
            let t0 = TaskState.Create<_,_>  //initial task state
                        ot.task.id
                        bus
                        driver
                        ot.task.cua.Value
                        ot.task.reasoner
                        planRun.kernel
            match ot.task.target with
            | OLink url -> do! driver.start url
            | OProcess (a,b) -> ()
            let flow = PlanFlow.create t0
            flow.Post PlanFlow.TFi_Start
            startTimer ot.task.allowedSec flow //sends task terminate message when this timer expires
            let! r = Async.AwaitWaitHandle(h,ot.task.allowedSec * 1000 * 3) //max wait for task to finish in case its stuck
            match completedTask.Value with
            | Some t -> return {ot with messages = t.cuaMessages}
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
                let tr = {task = t; driver = PlaywrightDriver.create().driver; messages=[]}

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
        let nav = Navigator()
        let mem = OPlanMemory()
        mem.SetMemory initialMemory
        b.Plugins.AddFromObject(mem) |> ignore
        b.Plugins.AddFromObject(nav) |> ignore
        b.Services.AddSingleton(nav) |> ignore
        b.Build()

    let rec run planRun = async {
        let nav = planRun.kernel.Services.GetService<Navigator>()
        nav.PlanRef.Value <- planRun
        let! planRun = step planRun
        if planRun.currentTask.IsSome then
            return! run planRun
        else
            return planRun
    }
