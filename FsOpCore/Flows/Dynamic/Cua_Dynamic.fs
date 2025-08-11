namespace FsOpCore.Dynamic
open System
open System.Text.Json
open FSharp.Control
open FsResponses
open FsOpCore

module Cua_Dynamic_Prompts =
    //a modification of OAI sample: see https://github.com/openai/openai-testing-agent-demo
    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.steps" />
    /// - <see cref="Vars.memory" />
    ///</summary>
    let ``cua loop`` = $"""
# [STEPS]
{{{{${Vars.steps}}}}}

# [MEMORY]
{{{{${Vars.memory}}}}}
"""


    //a modification of OAI sample: see https://github.com/openai/openai-testing-agent-demo
    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.cuaInstructions" />
    ///</summary>
    let ``cua step start`` = $"""You executing a task, which may a part of a larger sequence of tasks.
Tasks can pass data to other tasks via [MEMORY], if required.

The high-level instructions for the current task you are exectuing right now are given in [TASK_INSTRUCTIONS].

In addition, you will be given a list of step-by-step instructions which is a break down of the current task in [STEPS].

Focus on the 'ToDo' steps as 'Done' steps should already be completed. Only perform Optional steps if required.
Try to accomplish the steps in the simplest way possible.
Once you believe your are done with all the tasks required or you are blocked and cannot progress
(for example, you have tried multiple times to accomplish a task but keep getting errors or blocked),
use the task_done tool to let the user know you have finished the task.

**Note: You only have access to the 'task_done' tool. Don't attempt to call any other tools, even if instructed. **

# Normally, you do not need to authenticate on user's behalf, the user will authenticate and your flow starts after that.

Some steps may require information from [MEMORY]. Refer to memory, as needed, to complete steps.

# [TASK_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

"""

module CuaAgent = 
    type internal State = {prevId:string option; bus:WBus<TaskFlowMsgIn,TaskFlowMsgOut>}
        with
            static member Create bus = {prevId=None; bus=bus}

    //Handle all function calls found in response message.
    let callFunctions kernel resp = async {
        let fns =
            resp.output
            |> List.choose (function
                | IOitem.Function_call fn -> Some fn
                | _                       -> None)
        let mutable fouts = []
        for f in fns do
            let! rslt = Toolbox.invokeFunction kernel f.name f.arguments
            let fout = IOitem.Function_call_output {call_id = f.call_id; output = rslt}
            fouts <- fout::fouts
        return fouts
    }
    
    let MAX_SEQUENTIAL_FUNC_CALLS = 5
    let rec internal callFunctionLoop count state kernel resp =
        async {
            if count > MAX_SEQUENTIAL_FUNC_CALLS then 
                state.bus.PostToFlow( W_Err (WE_Error $"max repeated function calls count exceeded: {count}"))
                return None
            else 
                let! rslts = callFunctions kernel resp
                try 
                    let! resp = FlResps.sendWithRetry 0 {Request.Default with previous_response_id=Some resp.id; input=rslts}
                    if FlUtils.hasFunction resp then 
                        return! callFunctionLoop (count + 1) state kernel resp
                    else
                        return Some resp
                with ex ->
                    state.bus.PostToFlow(W_Err (WE_Exn ex))
                    return None
        }

    let internal processRequest state comp kernel =
        async {
            let! resp = comp
            match resp with
            | Some resp ->
                if FlUtils.hasFunction resp then 
                    let! resp' = callFunctionLoop 0 state kernel resp //handle function calls till no more (or max count reached)
                    return resp'
                else
                    return Some resp
            | None ->
                state.bus.PostToFlow( W_Err (WE_Error "responses api request failed"))
                return None
        }

    ///send a new cua request with 'computer tool call' - no prev state or history
    let internal sendReqStart (state:State) (req:CuaReq)=
       let vs = req.visualState
       async {
            let contImg = Content.Input_image {|image_url = vs.snapshot|}
            let input = { Message.Default with content=[contImg]}
            let cuaTool = Tool.Computer_use {|display_height = vs.height; display_width = vs.width; environment = vs.environment|}
            let req = {Request.Default with
                            input = [IOitem.Message input] @ (req.chatHistory |> List.map IOitem.Message)
                            tools = cuaTool :: req.nonCuaTools
                            instructions = req.instructions
                            previous_response_id = None
                            store = true
                            tool_choice = ToolChoice.Required
                            temperature = FlResps.temperature
                            reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            try
                let! resp = FlResps.sendWithRetry 0 req
                FlUtils.getUsage resp |> AGi_Usage |> W_Msg |> state.bus.PostToFlow
                return Some resp
            with _ ->
                return None
        }
    
    ///send a new cua request with 'computer tool call' - no prev state or history
    let internal sendReqLoop (state:State) (req:CuaReq) = async {
        let vs = req.visualState
        let cc = req.computerCall.Value
        let cuaTool = Tool.Computer_use {|display_height = vs.height; display_width = vs.width; environment = vs.environment|}
        let cc_out =
            {
                call_id = cc.call_id
                acknowledged_safety_checks = cc.pending_safety_checks 
                output = Computer_screenshot {|image_url = vs.snapshot |}
                current_url = vs.url
            }
            |> IOitem.Computer_call_output
        let req = {Request.Default with
                        input = cc_out :: (req.chatHistory |> List.map IOitem.Message)
                        tools = cuaTool :: req.nonCuaTools
                        instructions = req.instructions
                        previous_response_id = state.prevId 
                        store = true
                        tool_choice = ToolChoice.Required
                        temperature = FlResps.temperature
                        reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                        model=Models.computer_use_preview
                        truncation = Some Truncation.auto
                    }
        try
            let instr = req.instructions |> Option.iter  (printfn "%s")
            let! resp = FlResps.sendWithRetry 0 req
            FlUtils.getUsage resp |> AGi_Usage |> W_Msg |> state.bus.PostToFlow
            return Some resp
        with _ ->
            return None
    }

    let internal update state msg =
        async {
            match msg with
            | CUAo_Req r ->
                let comp = match r.computerCall with Some cc -> sendReqLoop state r | _ -> sendReqStart state r 
                match! processRequest state comp r.kernel  with 
                | Some resp -> match FlUtils.computerCall resp with
                               | Some cc -> state.bus.PostToFlow (W_Msg (CUAi_ComputerCall cc))
                                            return {state with prevId = Some resp.id}
                               | None    -> resp |> RUtils.outputText |> checkEmpty |> CUAi_NoComputerCall |> W_Msg |> state.bus.PostToFlow
                                            return {state with prevId = None} //need to reset CUA after no computer call
                | None -> state.bus.PostToFlow(W_Err (WErrorType.WE_Error "unable to get response from CUA model"))
                          return state
            | _ -> return state
        }
            
    let startCuaAgent (bus:WBus<TaskFlowMsgIn,TaskFlowMsgOut>) =
        let channel = bus.agentChannel.Subscribe("cua")
        channel.Reader.ReadAllAsync()
        |> AsyncSeq.ofAsyncEnum
        |> AsyncSeq.scanAsync update (State.Create bus)
        |> AsyncSeq.iter(fun _ -> ())
        |> FlResps.catch bus.PostToFlow

