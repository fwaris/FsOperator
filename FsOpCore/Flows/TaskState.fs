namespace FsOpCore
open FsResponses
open Microsoft.SemanticKernel

type Status =  ToDo = 0 | Done = 1
type CuaInstructionStep =
    {
        step_num: int
        step_instructions: string
        step_status : Status
    }

type CuaInstructions = {
    steps: CuaInstructionStep list
}

type CuaStep = {
    cuaMessages : ChatMsg list
    step : CuaInstructionStep
}
    with static member Create step = {cuaMessages=[]; step = step}

type CuaSteps = {
    stepIndex : int
    steps: CuaStep list
}
with
    static member Default = {stepIndex=0; steps=[]}
    member this.NextToDo() = this.steps |> List.tryFind (fun x -> x.step.step_status = Status.ToDo)
    member this.SetCurrentStep i = {this with stepIndex = i |> max 0 |> min this.steps.Length};
    member this.CurrentStep() = this.steps |> List.tryFind (fun s->s.step.step_num = this.stepIndex)
    member this.CurrentInstruction() =
        match this.CurrentStep() with
        | Some s -> s.step.step_instructions
        | None -> "no instruction available"
    member this.PrependCuaMessage msg =
        this.CurrentStep()
        |> Option.map (fun s ->
            let s = {s with cuaMessages = msg::s.cuaMessages}
            {this with steps = this.steps |> List.updateAt this.stepIndex s})
        |> Option.defaultValue this
    member this.AdvanceStep() =
        if this.stepIndex <= this.steps.Length - 1 then
            let s = this.steps.[this.stepIndex]
            let steps = this.steps |> List.updateAt this.stepIndex {s with step.step_status = Status.Done}
            {steps = steps ; stepIndex = this.stepIndex + 1 }
        else
            this

type CuaInstructionsResponse = {
    task_complete : bool
    cua_guidance : string
}

///reuseable state needed to keep track of a running task
type TaskState<'inMsg,'outMsg> = {
        id              : string
        target          : string
        cuaMessages     : ChatMsg list
        cuaPrompt       : string
        steps           : CuaSteps
        reasonerItems   : IOitem list
        reasonerPrevId  : string option
        reasonerPrompt  : string option
        driver          : IUIDriver
        actions         : string list
        kernel          : Kernel
        bus             : WBus<'inMsg,'outMsg>
        usage           : Map<string,FsResponses.Usage list>
        toolDefs        : Function list
    }
    with
        static member Create id target bus driver cuaPrompt reasonerPrompt kernel tools =
                            {
                               id = id
                               target = target
                               driver = driver
                               cuaMessages = []
                               cuaPrompt = cuaPrompt
                               reasonerPrompt = reasonerPrompt
                               kernel = kernel
                               reasonerItems = []
                               reasonerPrevId = None
                               actions = []
                               bus = bus
                               steps = CuaSteps.Default
                               usage = Map.empty
                               toolDefs = tools
                            }

        member this.prependCuaMessage msg = {this with cuaMessages = msg::this.cuaMessages}
        member this.prependReasonerItems items = {this with reasonerItems = items}
        member this.setPrevId id = {this with reasonerPrevId = Some id}
        member this.prependAction a = {this with actions = a::this.actions |> List.truncate C.MAX_ACTIONS }
        member this.setSteps xs = {this with steps = {this.steps with steps = xs}}
        member this.clearReasonerHistory() = {this with reasonerPrevId = None; reasonerItems = []}

        member this.appendUsage (modelId,(usage:FsResponses.Usage)) =
            let us = this.usage |> Map.tryFind modelId |> Option.map(fun us -> usage::us) |> Option.defaultWith (fun _ -> [usage])
            let usage = this.usage |> Map.add modelId us
            {this with usage=usage}

        member this.prependSnapshot snapshot =
            let imageCntnt = Content.Input_image {|image_url = snapshot|}
            [IOitem.Message {Message.Default with content = [imageCntnt]}] |> this.prependReasonerItems

        member this.lastAction() = this.actions |> List.tryHead |> Option.map(fun x-> [x]) |> Option.defaultValue []

        member this.actionsString() =
            this.actions
            |> List.rev
            |> List.indexed
            |> List.map (fun (i,x) -> $"{i}: {x}")
            |> String.concat ","


        ///Reset local reasoner state (full state is kept on server with.responses api 'save=true')
        member this.resetReasonerState id = {this with reasonerPrevId = Some id; reasonerItems = []}
