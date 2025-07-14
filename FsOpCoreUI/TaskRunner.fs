namespace FsOpCoreUI
open Elmish
open System
open FSharp.Control
open FsOpCore
open Avalonia.FuncUI.Elmish
open Avalonia.Layout
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Elmish.ElmishHook
open Avalonia.Threading
open System.Threading.Channels

module TaskRunner = 
    type MsgOut = Error of string  //external component can receive these via bus
    type MsgIn = Stop | Start      //external component can send these via bus
    
    module internal TaskRunner =
        type Msg = Start | Stop | SetMemory of string | MsgFromFlow of TaskFlow.TaskFlowMsgOut | MsgIn of MsgIn
        type Model = 
            {
                    task:OTask; 
                    flow:IFlow<TaskFlow.TaskFlowMsgIn> option; 
                    memory:string
                    action : string
                    error : string option
                    mailbox : Ref<Msg->unit>
            }

        let post (mailbox:Channel<'msg>) (msg:'msg) = mailbox.Writer.TryWrite msg |> ignore
    
        let taskState memory (model:Model) =
            let driver = PlaywrightDriver.create()
            let bus = WBus.Create<TaskFlow.TaskFlowMsgIn,TaskFlow.TaskFlowMsgOut> (MsgFromFlow>>(model.mailbox.Value))
            let kernel = OPlan.defaultKernel memory None
            let tools = (FlUtils.makeFunctionTools<Functions.FsOpMemory>() @ FlUtils.makeFunctionTools<Functions.FsOpNavigator>()) 
            TaskState.Create<TaskFlow.TaskFlowMsgIn,TaskFlow.TaskFlowMsgOut>  //initial task state
                        model.task.id
                        (model.task.target.TargetString())
                        bus
                        driver.driver
                        model.task.cua.Value
                        (model.task.reasoner |> Option.orElse (Some Prompts.``reasoner prompt for cua guidance``))
                        kernel
                        tools

        let init (t,mailbox) ()= 
            {
                task = t; 
                flow=None; 
                memory=""; action="";
                error=None
                mailbox = mailbox
            }, 
            Cmd.none

        let startFlow (model:Model) =
            match model.flow with 
            | Some _ -> model,Cmd.none
            | None -> 
                let mem = FlUtils.parseMemory model.memory
                let t = taskState mem model 
                let flow = TaskFlow.create t
                {model with flow = Some flow},Cmd.none
        
        let stopFlow (model:Model) =
            match model.flow with 
            | Some f -> f.Terminate(); {model with flow = None}, Cmd.none
            | None -> model, Cmd.none

        let getMemory (t:TaskState<_,_>) =
            FlUtils.getMemory t.kernel

        let update (dispatchOut:MsgOut -> unit) msg model = 
            match msg with 
            | SetMemory m -> {model with memory=m}, Cmd.none
            | Start -> startFlow model
            | Stop -> stopFlow model
            | MsgIn (MsgIn.Start) -> model,Cmd.ofMsg Start
            | MsgIn (MsgIn.Stop) -> model,Cmd.ofMsg Stop
            | MsgFromFlow (TaskFlow.TFo_Action action) -> {model with action = action }, Cmd.none
            | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.WE_Exn e)) -> {model with error = Some e.Message }, Cmd.none
            | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.Other e)) -> {model with error = Some e }, Cmd.none
            | MsgFromFlow (TaskFlow.TFo_Error (WErrorType.WE_Responses e)) -> {model with error = Some e }, Cmd.none
            | MsgFromFlow (TaskFlow.TFo_Done t) -> {model with memory = getMemory t}, Cmd.ofMsg Stop
            | MsgFromFlow (TaskFlow.TFo_Usage u) -> model,Cmd.none
            | MsgFromFlow (TaskFlow.TFo_Paused u) -> model,Cmd.none


        let view (model:Model) dispatch =
            // ... build UI as above ...
            DockPanel.create [
                DockPanel.children [
                    
                    TextBox.create [
                        TextBox.multiline true                    
                        TextBox.text $"{model.memory}"
                        TextBox.onTextChanged (fun t ->  dispatch SetMemory (t))
                    ]            
                ]
            ]            


    let createBus postBack = WBus.Create<MsgOut,MsgIn>(postBack)

    // The Component wrapper: uses useElmish to run the MVU loop internally
    let view (t:OTask,dispatchOut:MsgOut->unit,dispatchIn:Ref<MsgIn->unit>) : IView =
        Component.create($"taskRunnder.{t.id}", fun ctx ->
            let post = ref(fun m -> ())
            let sub _ = Subscriptions.create $"taskRunnder.{t.id}" post
            dispatchIn.Value <- (TaskRunner.MsgIn>>post.Value)
            let model, dispatch = ctx.useElmish (TaskRunner.init (t,post),  TaskRunner.update dispatchOut, Program.withSubscription sub)
            // The view renders the current state and dispatch function
            TaskRunner.view model dispatch
        )

