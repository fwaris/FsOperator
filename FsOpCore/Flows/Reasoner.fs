namespace FsOpCore
open System
open FsResponses
open System.ComponentModel

module Reasoner =

    //Handle all function calls found in response message.
    let callFunctions task resp = async {
        let fns =
            resp.output
            |> List.choose (function
                | IOitem.Function_call fn -> Some fn
                | _                       -> None)
        let mutable fouts = []
        for f in fns do
            let! rslt = FlUtils.invokeFunction task.kernel f.name f.arguments
            let fout = IOitem.Function_call_output {call_id = f.call_id; output = rslt}
            fouts <- fout::fouts
        return task.prependReasonerItems fouts
    }

    ///<summary>
    ///</summary>
    let postFunctionCall correlationId task =
        async {
            let req = {Request.Default with
                                input = List.rev task.reasonerItems
                                instructions = None
                                tools = task.toolDefs |> List.map Tool.Function
                                previous_response_id = task.reasonerPrevId
                                store = true
                                model=Models.o4_mini
                                truncation = Some Truncation.auto
                                metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                            }
            do! FlResps.postRequestAndReplyToChannel Workflow.ReasonerMsgWithCorrId task.bus.PostInput req
        }
        |> FlResps.catch task.bus.PostInput


    ///<summary>
    ///Send a request to the reasoner model with the give correlationId (returned in response).<br />
    ///The request 'input' items are obtained from <see cref="TaskState.reasonerState" />
    ///</summary>
    let postToReasoner correlationId task (responseFormat : Type option) reasonerInstructions  =
        async {
            let req = {Request.Default with
                                input = List.rev task.reasonerItems
                                instructions = reasonerInstructions
                                tools = task.toolDefs |> List.map Tool.Function
                                previous_response_id = task.reasonerPrevId
                                store = true
                                model=Models.o4_mini
                                text = responseFormat |> Option.map RUtils.structuredFormat
                                truncation = Some Truncation.auto
                                metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                            }
            do! FlResps.postRequestAndReplyToChannel Workflow.ReasonerMsgWithCorrId task.bus.PostInput req
        }
        |> FlResps.catch task.bus.PostInput

    ///<summary>
    ///Send a request to the reasoner model with the give correlationId (returned in response).<br />
    ///The request 'input' items are obtained from <see cref="TaskState.reasonerState" />
    ///</summary>
    let postPromptToReasoner correlationId task (responseFormat : Type option) prompt  =
        async {
            let req =
                {Request.Default with
                    input = [IOitem.Message {Message.Default with content = [Content.Input_text {|text = prompt|}]}]
                    model = Models.o4_mini
                    metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                    text = responseFormat |> Option.map (fun t -> RUtils.structuredFormat t) 
                }
            do! FlResps.postRequestAndReplyToChannel Workflow.ReasonerMsgWithCorrId task.bus.PostInput req
        }
        |> FlResps.catch task.bus.PostInput

    ///cua may be stuck; use this to terminate early and summarize the progress the plan can continue
    let stopAndSummarizeStep task =
        let id = newId()
        [
            Vars.taskInstructions,task.cuaPrompt :> obj
            Vars.steps, Text.Json.JsonSerializer.Serialize(task.steps.steps, FlUtils.openAIResponseSerOpts)
            Vars.memory, FlUtils.getMemory task.kernel
        ]
        |> Prompts.kernelArgs
        |> Prompts.renderPrompt Prompts.``cua early termination prompt step``
        |> postPromptToReasoner id task None
        id

    ///ask reasoner to break the CUA instructions into multiple smaller steps
    let breakTaskIntoSteps task =
        let id = newId()
        async {
            [
                Vars.cuaInstructions, task.cuaPrompt :> obj
            ]
            |> Prompts.kernelArgs
            |> Prompts.renderPrompt Prompts.``divide cua instructions into steps``
            |> postPromptToReasoner id task (Some typeof<CuaInstructions>)
        }
        |> FlResps.catch task.bus.PostInput
        id

    ///send message to reasoner to get guidance for cua for
    let getGuidanceForCuaStep task reasonerPrompt =
        let id = newId()
        async {
            let cuaMessageHistory =
                task.steps.CurrentStep()
                |> Option.map(fun s -> s.cuaMessages)
                |> Option.defaultValue []
                |> List.map (function
                    | User c -> $"user: {c}"
                    | Assistant m -> $"assistant: {m.content}")
                |> String.concat System.Environment.NewLine
            let args =
                Prompts.kernelArgs
                    [
                        Vars.cuaInstructions, task.steps.CurrentInstruction()
                        Vars.actionHistory,task.actionsString()
                        Vars.cuaMessageHistory,cuaMessageHistory
                        Vars.memory, FlUtils.getMemory task.kernel
                    ]
            let instructions = Prompts.renderPrompt reasonerPrompt args
            postToReasoner id task (Some typeof<CuaInstructionsResponse>) (Some instructions)
        }
        |> FlResps.catch task.bus.PostInput
        id

    ///send message to reasoner to get guidance for cua for the next action
    let getGuidanceForCuaNextAction task reasonerPrompt =
        let id = newId()
        async {
            let cuaMessageHistory =
                task.cuaMessages
                |> List.map (function
                    | User c -> $"user: {c}"
                    | Assistant m -> $"assistant: {m.content}")
                |> String.concat System.Environment.NewLine
            let args =
                Prompts.kernelArgs
                    [
                        Vars.cuaInstructions, task.cuaPrompt
                        Vars.actionHistory,task.actionsString()
                        Vars.cuaMessageHistory,cuaMessageHistory
                        Vars.memory, FlUtils.getMemory task.kernel
                    ]
            let instructions = Prompts.renderPrompt reasonerPrompt args
            postToReasoner id task (Some typeof<CuaInstructionsResponse>) (Some instructions)
        }
        |> FlResps.catch task.bus.PostInput
        id

    ///ask reasoner to respond to cua as a user would, to continue cua after pause
    let getGuidanceAfterCuaPause task =
        let id = newId()
        async {
            let cuaMessageHistory =
                task.cuaMessages
                |> List.map (function
                    | User c -> $"user: {c}"
                    | Assistant m -> $"assistant: {m.content}")
                |> String.concat System.Environment.NewLine
            let args =
                Prompts.kernelArgs
                    [
                        Vars.cuaInstructions, task.cuaPrompt
                        Vars.actionHistory,task.actionsString()
                        Vars.cuaMessageHistory,cuaMessageHistory
                        Vars.memory, FlUtils.getMemory task.kernel
                    ]
            let instructions = Prompts.renderPrompt Prompts.``resume cua after pause`` args
            postToReasoner id task (Some typeof<CuaInstructionsResponse>) (Some instructions)
        }
        |> FlResps.catch task.bus.PostInput
        id

    ///ask reasoner to respond to cua as a user would, to continue cua after pause in the current step
    let getGuidanceAfterCuaStepPause task =
        let id = newId()
        async {
            let cuaMessageHistory =
                task.steps.CurrentStep()
                |> Option.map(fun step ->
                    step.cuaMessages
                    |> List.map (function
                        | User c -> $"user: {c}"
                        | Assistant m -> $"assistant: {m.content}")
                    |> String.concat System.Environment.NewLine)
                |> Option.defaultValue ""
            let args =
                Prompts.kernelArgs
                    [
                        Vars.cuaInstructions, task.steps.CurrentInstruction()
                        Vars.actionHistory,task.actionsString()
                        Vars.cuaMessageHistory,cuaMessageHistory
                        Vars.memory, FlUtils.getMemory task.kernel
                    ]
            let instructions = Prompts.renderPrompt Prompts.``resume cua after pause`` args
            postToReasoner id task (Some typeof<CuaInstructionsResponse>) (Some instructions)
        }
        |> FlResps.catch task.bus.PostInput
        id

    ///returns None if reasoner says task is done otherwise Some 'new instructions'
    let reasonerGuidance resp =
        try
            let resp = RUtils.parseContent<CuaInstructionsResponse> resp //get structured output
            match resp with
            | None -> Some $"reasoner model did not send appropriate resp. for cua guidance. Please retry"
            | Some (Choice2Of2 e) -> Some $"reasoner model refused to provide structured output '{e}'. Please retry"
            | Some (Choice1Of2 cuaInstr) ->
                if cuaInstr.task_complete then
                    None
                else
                    Some cuaInstr.cua_guidance
        with ex -> Some "unable to parse model response. Please retry"


    ///cua may be stuck; use this to terminate early and summarize the progress the plan can continue
    let stopAndSummarize task =
        let id = newId()
        Prompts.kernelArgs [Vars.taskInstructions,task.cuaPrompt]
        |> Prompts.renderPrompt Prompts.``cua early termination prompt``
        |> Some
        |> postToReasoner id task None
        id

