namespace FsOperator
open FsOpCore
open Microsoft.SemanticKernel
open Elmish

module Flows = 

    let terminateFlow model = 
        let voiceAsst = 
            model.voiceAsst
            |> Option.map RTOpenAI.Api.Connection.close
            |> Option.map RTOpenAI.Api.Connection.create
        {model with flow = model.flow.Terminate(); voiceAsst = voiceAsst},Cmd.none

    let startVoiceFlow model conn taskState = 
        let voicePrompt = 
            checkEmpty model.opTask.voiceAsstInstructions
            |> Option.orElse (Some Prompts.``starting voice prompt``)
            |> Option.map (fun t -> 
                t,  [
                        Vars.taskInstructions, model.opTask.textModeInstructions :> obj
                        Vars.startUrl, model.opTask.target.TargetString()
                    ])
            |> Option.map (fun (t,args) -> Prompts.renderPrompt t args)
        let taskState = {taskState with toolDefs = taskState.toolDefs @ Toolbox.makeFunctionTools<Functions.FsOpVoice>()}
        let flow = TaskFlowInteractive.create taskState (Some conn) voicePrompt
        let model = {model with flow = {Flow.Default with state=FL_Flow {|flow=flow|}}}            
        async {
            do! Async.Sleep 100
            flow.Post TaskFlowInteractive.TFi_Prime
        } 
        |> Async.Start
        model,Cmd.none

    let configVoice driver bus  (b:IKernelBuilder) =
        let funcs = TaskFlowInteractive.createVoiceFunctions driver bus
        let voice = Functions.FsOpVoice()
        voice.SetFunctions(funcs)
        b.Plugins.AddFromObject(voice) |> ignore


    let startTextFlow model taskState  =
        let flow = TaskFlowInteractive.create taskState None None
        let model = {model with flow = {Flow.Default with state=FL_Flow {|flow=flow|}}}            
        async {
            do! Async.Sleep 100
            flow.Post TaskFlowInteractive.TFi_Prime
        } 
        |> Async.Start
        model,Cmd.none

    let startFlow (model:Model) =
        let taskState = lazy(
            let driver = PlaywrightDriver.create()
            let bus = WBus.Create<_,_> (Flow_Msg>>model.post)
            let kernel = OPlan.defaultKernel Map.empty (Some (configVoice driver.driver bus ))
            let tools = 
                Toolbox.makeFunctionTools<Functions.FsOpMemory>() 
                @ Toolbox.makeFunctionTools<Functions.FsOpNavigator>()
            FsOpCore.TaskState.Create<_,_>  //initial task state
                        model.opTask.id
                        (model.opTask.target.TargetString())
                        bus
                        driver.driver
                        model.opTask.textModeInstructions
                        (checkEmpty model.opTask.reasonerInstructions |> Option.orElse (Some Prompts.``reasoner prompt for cua guidance``))
                        kernel
                        tools)
        match model.voiceAsst, checkEmpty model.opTask.textModeInstructions, OpTask.isTargetNotEmpty model.opTask.target with
        | Some conn, _, _ -> startVoiceFlow model conn taskState.Value
        | None, Some instr, true -> startTextFlow model taskState.Value
        | _, None, _ -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start task, no text-mode instructions given")
        | _,_,false    -> model, Cmd.ofMsg (StatusMsg_Set "Cannot start task in text mode, no start URL provided")


    let voiceToggle model = 
        let voiceAsst = 
            match model.voiceAsst with 
            | Some v -> RTOpenAI.Api.Connection.close v; None
            | None   -> Some (RTOpenAI.Api.Connection.create())
        {model with voiceAsst = voiceAsst}, Cmd.none
