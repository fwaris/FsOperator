namespace FsOpCore.Dynamic
open System
open FsOpCore
open System.Text.Json
open FsResponses

module Prompts_Dynamic =
    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.cuaInstructions" /><br />
    ///</summary>
    let ``[dvlpr] reasoner start instructions`` = $"""The Computer Use Agent (CUA) follows a set of instructions to complete a task by issuing commands like 'click', 'move', or 'type text', etc., by observing the screenshots of the target environment.

CUA may not always follow instructions accurately.

Your Job:
Drive CUA to accomplish the task described in [TASK_INSTRUCTIONS].

# [TASK_INSTRUCTIONS]
```
{{{{${Vars.cuaInstructions}}}}}
```

## Miscellaneous:
CUA does not have the ability to call functions. Instead of asking CUA to invoke functions, you just invoke the functions directly.
To save and retrieve memory, use the functions provided.
Extract relevant textual information from the screenshots images provided and save to memory if needed
CUA cannot focus on the browser's address bar; to get the browser page url use the 'get_url' function.

## Memory:
You can read/write from/to memory using the functions provided to save relevant facts for later tasks.
However, any existing memory saved before this task is already provided in [MEMORY].

Assume that CUA only has access to a Web Brower (not the whole computer).

If you believe the task is done as per [TASK_INSTRUCTIONS], invoke the task_done tool to end the task. Ensure the task is truly done.

"""

    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.memory" /><br />
    ///</summary>
    let ``[instr] initial steps`` = $"""Generate the initial 3 steps that CUA should follow
given the [TASK_INSTRUCTIONS] and the current [MEMORY] content. Mark optional steps as such.

# [MEMORY]
```
{{{{${Vars.memory}}}}}
```
"""

    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.memory" /><br />
    /// - <see cref="Vars.actionHistory" /><br />
    /// - <see cref="Vars.cuaMessageHistory" />
    ///</summary>
    let ``[instr] get next steps`` = $"""Given the [TASK_INSTRUCTIONS], the current [MEMORY], snapshot / action / message histories, generate the immediate next step(s) that CUA should follow. Do not exceed 3 steps. If you think the task
    is done, return an empty list.

# [MEMORY]
```
{{{{${Vars.memory}}}}}
```

# CUA Action History:
```
{{{{${Vars.actionHistory}}}}}
```

# CUA Message History:
```
{{{{${Vars.cuaMessageHistory}}}}}
```
"""


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

module Reasoner_Dynamic =
    ///ask reasoner to break the CUA instructions into multiple smaller steps
    let getInitialSteps task =
        let correlationId = newId()
        async {
            let msg =
                [
                    Vars.cuaInstructions, task.cuaPrompt :> obj
                ]
                |> Prompts.renderPrompt Prompts_Dynamic.``[dvlpr] reasoner start instructions``
            let msg = {Message.Default with content = [Content.Input_text {|text = msg|}]; role="developer"}
            let instr =
                [Vars.memory, FlUtils.getMemory task.kernel :> obj]
                |> Prompts.renderPrompt Prompts_Dynamic.``[instr] initial steps``
            let req =
                {Request.Default with
                    input = [IOitem.Message msg]
                    model=Models.o4_mini
                    instructions = Some instr
                    store = true
                    text = RUtils.structuredFormat typeof<CuaInstructions> |> Some
                    metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                }
            do! FlResps.postRequestAndReplyToChannel Workflow.ReasonerMsgWithCorrId task.bus.PostInput req
        }
        |> FlResps.catch task.bus.PostInput
        correlationId

    let getNextSteps task =
        let correlationId = newId()
        async {
            let instructions =
                [
                    Vars.memory, FlUtils.getMemory task.kernel :> obj
                    Vars.actionHistory, task.actionsString()
                    Vars.cuaMessageHistory, (string task.cuaMessages)
                ]
                |> Prompts.renderPrompt Prompts_Dynamic.``[instr] get next steps``

            let inp = List.rev task.reasonerItems |> List.sortBy (function IOitem.Function_call_output _ -> 0 | _ -> 1) //put function all outputs first
            let req =
                {Request.Default with
                    input = inp
                    instructions = Some instructions
                    tools = task.toolDefs |> List.map Tool.Function
                    previous_response_id = task.reasonerPrevId
                    store = true
                    parallel_tool_calls = true
                    model=Models.o4_mini
                    text = RUtils.structuredFormat typeof<CuaInstructions> |> Some
                    truncation = Some Truncation.auto
                    metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                }
            do! FlResps.postRequestAndReplyToChannel Workflow.ReasonerMsgWithCorrId task.bus.PostInput req
        }
        |> FlResps.catch task.bus.PostInput
        correlationId

module Cua_Dynamic = 

    let startStep visualState (task:TaskState<_,_>) =
        let instructions = 
            [
                Vars.steps, task.serializeSteps():> obj
                Vars.memory,FlUtils.getMemory task.kernel
            ]
            |> Prompts.renderPrompt Prompts_Dynamic.``cua loop``
        let developerPrompt = 
            [
                Vars.cuaInstructions, task.cuaPrompt :> obj
            ]
            |> Prompts.renderPrompt Prompts_Dynamic.``cua step start``
        let chatHistory = [Developer developerPrompt] |> FlResps.toMessages
        let req =
            {CuaReq.Default with 
                chatHistory = chatHistory
                instructions = (Some instructions)                                                          
                visualState  = visualState
            }
        FlResps.postStartCuaRequest task.bus.PostInput req

    let postCuaNextStep task vs (cuaResp:Response) =

        match vs, FlUtils.computerCall cuaResp with
        | Some vs, Some cc ->
            let cuaTool = Tool.Computer_use {|display_height = vs.height; display_width = vs.width; environment = vs.environment|}
            let cc_out =
                {
                    call_id = cc.call_id
                    acknowledged_safety_checks = FlResps.safetyChecks cuaResp
                    output = Computer_screenshot {|image_url = vs.snapshot |}
                    current_url = vs.url
                }
                |> IOitem.Computer_call_output
            let inp = cc_out::List.rev task.cuaItems |> List.sortBy (function IOitem.Function_call_output _ -> 0 | _ -> 1) //put function call outputs first
            let instructions = 
                [
                    Vars.steps, task.serializeSteps() :> obj
                    Vars.memory,FlUtils.getMemory task.kernel
                ]
                |> Prompts.renderPrompt Prompts_Dynamic.``cua loop``
            let req = {Request.Default with
                            input = inp
                            instructions = Some instructions
                            tools = [cuaTool]
                            previous_response_id = Some cuaResp.id
                            parallel_tool_calls = true
                            store = true
                            tool_choice = ToolChoice.Required
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                      }
            FlResps.postRequestAndReplyToChannel W_Cua task.bus.PostInput req
        | None,_ -> async {return failwith "no 'visual state' e.g. sceenshot width, height, given"}
        | _,None -> async {return failwith "no computer call output found in response"}
        |> FlResps.catch task.bus.PostInput
