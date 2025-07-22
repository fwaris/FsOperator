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
with
    member this.NextToDo() = this.steps |> List.tryFind (fun x -> x.step_status = Status.ToDo)


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
        steps           : CuaInstructions
        reasonerItems   : IOitem list
        cuaItems        : IOitem list
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
                               cuaItems = []
                               reasonerPrevId = None
                               actions = []
                               bus = bus
                               steps = {steps=[]}
                               usage = Map.empty
                               toolDefs = tools
                            }

        member this.prependCuaMessage (msg:ChatMsg) = {this with cuaMessages = msg::this.cuaMessages}
        member this.prependReasonerItems items = {this with reasonerItems = items @ this.reasonerItems}
        member this.prependCuaItems items = {this with cuaItems = items @ this.cuaItems}
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
        member this.resetCuaItems() = {this with cuaItems = []}
        member this.resetReasonerItems() = {this with reasonerItems = []}
