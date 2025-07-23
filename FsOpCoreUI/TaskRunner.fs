namespace FsOpCoreUI
open Elmish
open System
open FsOpCore
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Elmish.ElmishHook
open Avalonia.Media
open System.Threading.Channels

module TaskRunner = 
    type MsgOut = Error of string  //parent component can receive these messasges
    type MsgIn = Stop | Start      //parent component can send these messages
    
    module internal TaskRunner =
        type Msg = Start | Stop | SetMemory of string | Error of string | MsgFromFlow of TaskFlow.TaskFlowMsgOut | MsgIn of MsgIn
        type Model = 
            {
                    task:IReadable<OTask>
                    running : IWritable<bool>
                    flow:IFlow<TaskFlow.TaskFlowMsgIn> option; 
                    memory:string
                    log : string list
                    action : string
                    error : string option
                    wfReceiver : IReadable<Ref<TaskFlow.TaskFlowMsgOut->unit>>
            }
    
        let taskState memory (model:Model) =
            let task = model.task.Current
            let driver = PlaywrightDriver.create()
            let bus = WBus.Create<TaskFlow.TaskFlowMsgIn,TaskFlow.TaskFlowMsgOut> model.wfReceiver.Current.Value
            let kernel = OPlan.defaultKernel memory None
            let tools = (Toolbox.makeFunctionTools<Functions.FsOpMemory>() @ Toolbox.makeFunctionTools<Functions.FsOpNavigator>()) 
            TaskState.Create<TaskFlow.TaskFlowMsgIn,TaskFlow.TaskFlowMsgOut>  //initial task state
                        task.id
                        (task.target.TargetString())
                        bus
                        driver.driver
                        task.cua.Value
                        (task.reasoner |> Option.orElse (Some Prompts.``reasoner prompt for cua guidance``))
                        kernel
                        tools

        let init (t,r,wfReceiver) ()= 
            {
                task        = t 
                running     = r
                flow        = None
                memory      = ""
                action      = ""
                error       = None
                wfReceiver   = wfReceiver
                log         = []
            }, 
            Cmd.none

        let startFlow (model:Model) =
            match model.flow with 
            | Some _ -> model,Cmd.none
            | None -> 
                let mem = FlUtils.parseMemory model.memory
                let t = taskState mem model 
                let flow = TaskFlow.create t
                model.running.Set(true)
                flow.Post TaskFlow.TaskFlowMsgIn.TFi_Start //posting this starts the flow
                {model with flow = Some flow},Cmd.none
        
        let stopFlow (model:Model) =
            match model.flow with 
            | Some f -> 
                model.running.Set(false)
                f.Terminate(); {model with flow = None}, Cmd.none
            | None -> model, Cmd.none

        let getMemory (t:TaskState<_,_>) =
            FlUtils.getMemory t.kernel

        let appendLog logEntry model = 
            {model with log = logEntry::model.log }, Cmd.none

        let update (dispatchOut:MsgOut -> unit) msg model = 
            try 
                match msg with 
                | SetMemory m -> {model with memory=m}, Cmd.none
                | Start -> startFlow model
                | Stop -> stopFlow model
                | Error e -> dispatchOut (MsgOut.Error e); model |> appendLog e
                | MsgIn (MsgIn.Start) -> model,Cmd.ofMsg Start
                | MsgIn (MsgIn.Stop) -> model,Cmd.ofMsg Stop
                | MsgFromFlow (TaskFlow.TFo_Action action) -> model |> appendLog $"action: {action}"
                | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.WE_Exn e)) -> model, Cmd.ofMsg (Error $"Flow exception: {e}")
                | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.Other e)) -> model, Cmd.ofMsg (Error $"Flow error: {e}")
                | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.WE_Responses e)) -> model, Cmd.ofMsg (Error $"Api error: {e}")
                | MsgFromFlow (TaskFlow.TFo_Done t) -> {model with memory = getMemory t}, Cmd.ofMsg Stop
                | MsgFromFlow (TaskFlow.TFo_Usage u) -> model,Cmd.none
                | MsgFromFlow (TaskFlow.TFo_Paused u) -> model,Cmd.none
            with ex ->
                Log.exn(ex,"TaskRunner")
                model, Cmd.ofMsg (Error $"error: ex.Message")

        let view (model:Model) dispatch =
            // ... build UI as above ...
            DockPanel.create [
                DockPanel.children [
                    TextBlock.create [
                        DockPanel.dock Dock.Top
                        TextBlock.text "Memory"
                        TextBlock.fontWeight FontWeight.Bold
                    ]
                    TextBox.create [                       
                        DockPanel.dock Dock.Top
                        TextBox.multiline true                    
                        TextBox.acceptsReturn true
                        TextBox.height 150.
                        TextBox.text $"{model.memory}"
                        TextBox.onTextChanged (fun t ->  dispatch (SetMemory t))
                    ]
                    Border.create [
                        Border.child (
                            DockPanel.create [
                                DockPanel.children [
                                    TextBlock.create [
                                        DockPanel.dock Dock.Top
                                        TextBlock.text "Log"
                                        TextBlock.fontWeight FontWeight.Bold
                                    ]
                                    TextBlock.create [
                                        DockPanel.dock Dock.Top
                                        TextBlock.text "(newest item at top)"
                                        TextBlock.fontSize 10.0
                                    ]
                                    ListBox.create [
                                        ListBox.dataItems model.log
                                    ]
                                ]
                            ]
                        )
                    ]
                ]
            ]            

    ///Parent component supplies IReadable,IWritable state variables that are shared with this component
    let view (task:IReadable<OTask>,running:IWritable<bool>,sendToMe:IReadable<Ref<MsgIn->unit>>,dispatchOut:MsgOut->unit) : IView =
        Component.create($"taskRunner", fun ctx ->
            
            //use 'usePassed/Read' hooks to re-wire this instance of the control to shared state from parent
            let task = ctx.usePassedRead task
            let running = ctx.usePassed running
            let sendToMe = ctx.usePassedRead sendToMe 
            
            let wfSendToMe = ctx.useStateLazy((fun () ->ref(fun m -> printfn $"TaskRunner.view default got {m}")), renderOnChange=false)
            //also create a subscription to post messages to the dispatch loop of this control,
            //from outside the control
            let sub _ = 
                Subscriptions.create $"taskRunner {task.Current.id}" 
                    (fun poster ->                                              //this function is invoked when the scription is created
                        sendToMe.Current.Value <- (TaskRunner.MsgIn>>poster)    //Wire the 'poster' function to parent so parent can send messages to this
                        wfSendToMe.Current.Value <- (TaskRunner.MsgFromFlow>>poster)) //Wire the poster to receive mesages from workflow

            let model, dispatch = ctx.useElmish (TaskRunner.init (task,running,wfSendToMe),  TaskRunner.update dispatchOut, Program.withSubscription sub)
            // The view renders the current state and dispatch function
            TaskRunner.view model dispatch
        )
