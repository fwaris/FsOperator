namespace FsOpCore.Dynamic
open System
open System.Text.Json
open FSharp.Control
open FsResponses
open FsOpCore

module Reasoner_Dynamic_Prompts =
    //NOTE: We are assuming that 'developer' prompts don't go out scope when max context length is breached, in the 'responses' api.

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

Additional Notes:
- CUA occasionally may not be able to scroll by issuing the scroll command. If you detect that then suggest page up/down or dragging the scroll bar.
```

## Miscellaneous:
- CUA does not have the ability to call tools. Instead of asking CUA to invoke tools, you just invoke the tools directly.
- CUA cannot focus on the browser's address bar; to get the browser page url use the 'get_url' tool.
- If you think CUA is waiting too long for the home page to load, use the 'home' tool to force loading it.
- If no login credentials are given and the web page is asking for the user to login then ask CUA to issue 'wait', till the login process is complete.

## Memory:
Extract relevant textual information from the screenshots images provided and save to memory if needed
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
    let ``[instr] get next steps`` = $"""Given the [TASK_INSTRUCTIONS], the current [MEMORY], snapshot / action / message histories, generate the immediate next step(s) that CUA should follow. Do not exceed 2 steps. If you think the task
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

    let ``cua early termination prompt`` = $"""The user has asked Computer Use Agent (CUA) to
end processing. 

Give the current context (screenshots, any message and action  history) summarize the content
in relation to the original [TASK_INSTRUCTIONS].
"""

module ReasonerAgent =

    type internal State =
        {
            prevId             : string option
            bus                : WBus<TaskFlowMsgIn,TaskFlowMsgOut>
        }
        with static member Create bus = 
                        {
                            prevId = None
                            bus = bus
                        }

    let internal createReqInitial state (req:ReasonerReq) = 
        let msg =
            [
                Vars.cuaInstructions, req.cuaPrompt :> obj
            ]
            |> Prompts.renderPrompt Reasoner_Dynamic_Prompts.``[dvlpr] reasoner start instructions``
        let msg = {Message.Default with content = [Content.Input_text {|text = msg|}]; role="developer"}
        let instr =
            [Vars.memory, req.memory :> obj]
            |> Prompts.renderPrompt Reasoner_Dynamic_Prompts.``[instr] initial steps``
        let req =
            {Request.Default with
                input = [IOitem.Message msg]
                model=C.REASONER_MODEL
                instructions = Some instr
                reasoning = Some {Reasoning.Default with effort=Some Reasoning.High}
                store = true
                text = RUtils.structuredFormat typeof<CuaInstructions> |> Some
            }
        req

    let internal createReqNextSteps state (req:ReasonerReq) =
        let instructions =
            [
                Vars.memory, req.memory :> obj
                Vars.actionHistory, req.actions
                Vars.cuaMessageHistory, (string req.cuaMessages)
            ]
            |> Prompts.renderPrompt Reasoner_Dynamic_Prompts.``[instr] get next steps``

        let inp = List.rev req.items |> List.sortBy (function IOitem.Function_call_output _ -> 0 | _ -> 1) //put function all outputs first
        let req =
            {Request.Default with
                input = inp
                instructions = Some instructions
                tools = req.toolDefs |> List.map Tool.Function
                previous_response_id = state.prevId
                store = true
                parallel_tool_calls = true
                model=C.REASONER_MODEL
                text = RUtils.structuredFormat typeof<CuaInstructions> |> Some
                truncation = Some Truncation.auto
            }
        req

    let internal invokeApi state req =
        async {
            try
                let! resp = FlResps.sendWithRetry 0 req
                FlUtils.getUsage resp |> AGi_Usage |> W_Msg |> state.bus.PostToFlow
                return Some resp
            with _ ->
                return None
        }

    let internal createSummarizeReq state (req:ReasonerReq) =
        let summarizeMsg = Message.OfText Reasoner_Dynamic_Prompts.``cua early termination prompt``
        let inp = List.rev req.items |> List.sortBy (function IOitem.Function_call_output _ -> 0 | _ -> 1) //put function all outputs first
        let req =
            {Request.Default with
                input = inp @ [IOitem.Message summarizeMsg]
                previous_response_id = state.prevId
                store = true
                tool_choice = ToolChoice.None
                model=C.REASONER_MODEL
            }
        req

    let extractSteps resp =
        let text = RUtils.outputText resp
        match checkEmpty text with
        | Some text ->
            try
                let xs = JsonSerializer.Deserialize<CuaInstructions>(text, Utility.openAIResponseSerOpts)
                xs.steps
            with ex ->
                Log.exn (ex,"ReasonerAgent.extractSteps")
                []
        | None ->
            Log.info $"Empty steps from reasoner call"
            []

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
    let rec internal callFunctionLoop state kernel protoReq resp count =
        async {
            if count > MAX_SEQUENTIAL_FUNC_CALLS then 
                state.bus.PostToFlow( W_Err (WE_Error $"max repeated function calls count exceeded: {count}"))
                return None
            else 
                let! rslts = callFunctions kernel resp
                try 
                    let req = {protoReq with input=rslts; previous_response_id=Some resp.id}
                    let! resp = FlResps.sendWithRetry 0 req
                    if FlUtils.hasFunction resp then 
                        return! callFunctionLoop state kernel protoReq resp (count + 1)
                    else
                        return Some resp
                with ex ->
                    state.bus.PostToFlow(W_Err (WE_Exn ex))
                    return None
        }

    let internal processRequest state kernel req =
        async {
            let! resp = invokeApi state req
            match resp with
            | Some resp ->
                if FlUtils.hasFunction resp then 
                    let! resp' = callFunctionLoop state kernel req resp 0 //handle function calls till no more (or max count reached)
                    return resp'
                else
                    return Some resp
            | None ->
                state.bus.PostToFlow( W_Err (WE_Error "responses api request failed"))
                return None
        }

    let internal update state msg =
        async {
            match msg with
            | RSNRo_GetSteps r ->
                let req = if state.prevId.IsNone then createReqInitial state r else createReqNextSteps state r                
                match! processRequest state r.kernel req with 
                | Some resp ->
                    let state = {state with prevId = Some resp.id}
                    let steps = extractSteps resp
                    state.bus.PostToFlow (W_Msg (RSNRi_Steps steps))
                    return state
                | None -> 
                    return state
            | RSNRo_Summarize r ->
                    let req = createSummarizeReq state r
                    match! processRequest state r.kernel req with 
                    | Some resp -> 
                        resp |> RUtils.outputText |> RSNRi_Summary |> W_Msg |> state.bus.PostToFlow
                        return {state with prevId = Some resp.id}
                    | None -> 
                        return state
            | _ -> 
                return state
        }

    let startReasonerAgent (bus:WBus<TaskFlowMsgIn,TaskFlowMsgOut>) =
        let channel = bus.agentChannel.Subscribe("reasoner")
        channel.Reader.ReadAllAsync()
        |> AsyncSeq.ofAsyncEnum
        |> AsyncSeq.scanAsync update (State.Create bus)
        |> AsyncSeq.iter(fun _ -> ())
        |> FlResps.catch bus.PostToFlow
