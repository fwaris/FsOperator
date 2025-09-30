namespace TMOnboard.Flow
open System
open Microsoft.SemanticKernel
open FSharp.Control
open System.Threading
open Microsoft.Extensions.DependencyInjection
open FsOpCore

module PlanRunner =
    open System.Threading.Channels

    let monitorTask (planChannel:Channel<TaskFlowMsgOut>) (h:ManualResetEvent) (completedTask:Ref<TaskState<_,_> option>) (bus:WBus<_,_>) =
        let comp =
            let channel = bus.agentChannel.Subscribe("app")
            channel.Reader.ReadAllAsync()
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun msg -> async {
                planChannel.Writer.TryWrite(msg) |> ignore
                match msg with
                | TaskFlowMsgOut.APo_Done t -> 
                    completedTask.Value <- Some t
                    h.Set() |> ignore
                    do! t.driver.saveState()
                | TaskFlowMsgOut.APo_Error e -> printfn "%A" e;  h.Set() |> ignore
                | TaskFlowMsgOut.APo_Action a -> printfn "%A" a
                | TaskFlowMsgOut.APo_Usage us -> OPlan.printTaskUsage us
                | _ -> () //ignore messages for other agents
            })
        async {
            match! Async.Catch(comp) with
            | Choice1Of2 _ -> Log.info "task ended"
            |  Choice2Of2 ex -> Log.exn(ex,"monitorTask")
        }
        |> Async.Start

    let startTimer (n:int) (f:IFlow<_>) =
        async {
            do! Async.Sleep (n * 1000)
            f.Post TaskFlowMsgIn.APi_EndAndReport
        }
        |> Async.Start

    
    ///Runs the current task set in planRun
    let runCurrentTask (planChannel:Channel<TaskFlowMsgOut>) (planRun:OPlanRun) = async{
        match planRun.currentTask with
        | None -> return failwith $"no task to run"
        | Some ot ->
            use h = new ManualResetEvent(false)
            let completedTask = ref None
            let driver = (PlaywrightDriver.create().driver)
            let bus = WBus.Create<_,_>()
            let t0 = TaskState.Create<_,_>  //initial task state
                        ot.task.id
                        (ot.task.target.TargetString())
                        bus
                        driver
                        ot.task.cua.Value
                        ot.task.reasoner
                        planRun.kernel
                        ot.task.tools
            let flow = TaskFlow_PortPin.create t0
            flow.Post TaskFlowMsgIn.APi_Start
            monitorTask planChannel h completedTask bus
            startTimer ot.task.allowedSec flow //sends task terminate message when this timer expires
            let! r = Async.AwaitWaitHandle(h,ot.task.allowedSec * 1000 * 3) //max wait for task to finish in case its stuck
            match completedTask.Value with
            | Some t -> return {ot with messages = t.cuaMessages; usage = OPlan.sumUsages t.usage}
            | None   -> return failwith "no output from step"
    }

    ///Single step the plan: Transition to next task and run it or end if no task.
    let step planChannel planRun = async {
        match! OPlan.transitionToNext planRun with
        | None ->
            return
                {planRun with
                    currentTask = None
                    completedTasks = OPlan.appendTask planRun.currentTask planRun.completedTasks}
        | Some t ->
                let tr = OTaskRun.Create t (planRun.driver)
                let planRun =
                        {planRun with
                            currentTask = Some tr
                            completedTasks = OPlan.appendTask planRun.currentTask planRun.completedTasks
                         }
                let! tr' = runCurrentTask planChannel planRun
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
        let ttls = Functions.FsOpTaskTools()
        mem.SetMemory initialMemory
        b.Plugins.AddFromObject(ttls) |> ignore
        b.Plugins.AddFromObject(mem) |> ignore
        b.Plugins.AddFromObject(nav) |> ignore
        //some of the plugins are also added as services so that they can be accessed internally
        b.Services.AddSingleton(nav) |> ignore 
        b.Services.AddSingleton(ttls) |> ignore
        b.Services.AddSingleton(mem) |> ignore
        b.Build()

    let rec run planChannel planRun = async {
        let t0 = DateTime.Now
        let planRun : OPlanRun = { planRun with startTime = Some t0 }
        let! planRun = step planChannel planRun
        if planRun.currentTask.IsSome then
            return! run planChannel planRun
        else
            do! PlaywrightDriver.shutdown() 
            let t1 = DateTime.Now
            let planRun = { planRun with endTime = Some t1 }
            planChannel.Writer.TryComplete() |> ignore
            return planRun
    }
