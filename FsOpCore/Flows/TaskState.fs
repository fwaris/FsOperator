namespace FsOpCore
open FsResponses
open System.Text.Json
open Microsoft.SemanticKernel

type Status =  ToDo = 0 | Done = 2
type Requirement = Optional = 0 | Required = 1
type CuaInstructionStep =
    {
        step_num: int
        step_required : Requirement
        step_instructions: string
        step_status : Status
    }

type CuaInstructions = {
    steps: CuaInstructionStep list
}
with
    member this.NextToDo() = 
        this.steps 
        |> List.filter (fun x -> x.step_required = Requirement.Required) 
        |> List.tryFind (fun x -> x.step_status = Status.ToDo)

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
        steps           : CuaInstructions option
        reasonerItems   : IOitem list
        reasonerPrompt  : string option
        driver          : IUIDriver
        actions         : string list
        kernel          : Kernel
        bus             : WBus<'inMsg,'outMsg>
        usage           : Map<string,FsResponses.Usage list>
        toolDefs        : Function list
        visualState     : VisualState option
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
                               actions = []
                               bus = bus
                               steps = None
                               usage = Map.empty
                               toolDefs = tools
                               visualState = None
                            }

        member this.prependCuaMessage (msg:ChatMsg) = {this with cuaMessages = msg::this.cuaMessages}
        member this.prependReasonerItems items = {this with reasonerItems = items @ this.reasonerItems}
        member this.prependAction a = {this with actions = a::this.actions |> List.truncate C.MAX_ACTIONS }
        member this.setSteps xs = {this with steps = match this.steps with Some s -> Some {s with steps = xs} | None -> Some {steps=xs}}
        member this.serializeSteps() = match this.steps with Some s -> Utility.formatJson s | _-> ""
        //JsonSerializer.Serialize(s.steps,FlUtils.openAIResponseSerOpts) | _ -> ""
        member this.NextToDo() = match this.steps with None -> Choice1Of2 () | Some s -> Choice2Of2 (s.NextToDo())
        member this.clearReasonerHistory() = {this with reasonerItems = []}

        member this.appendUsage (modelId,(usage:FsResponses.Usage)) =
            let us = this.usage |> Map.tryFind modelId |> Option.map(fun us -> usage::us) |> Option.defaultWith (fun _ -> [usage])
            let usage = this.usage |> Map.add modelId us
            {this with usage=usage}


        member this.lastAction() = this.actions |> List.tryHead |> Option.map(fun x-> [x]) |> Option.defaultValue []

        member this.actionsString() =
            this.actions
            |> List.rev
            |> List.indexed
            |> List.map (fun (i,x) -> $"{i}: {x}")
            |> String.concat ","


        ///Reset local reasoner state (full state is kept on server with.responses api 'save=true')
        member this.resetReasonerItems() = {this with reasonerItems = []}

        member this.acceptVisualState vs = let t = {this with visualState = Some vs} in t.prependSnapshot vs.snapshot

        member this.prependSnapshot snapshot =
            let imageCntnt = Content.Input_image {|image_url = snapshot|}
            [IOitem.Message {Message.Default with content = [imageCntnt]}] |> this.prependReasonerItems

        member this.performActionAndCapture (cc:ComputerCall) = async {
                do! Actions.doAction 2 this.driver cc.action
                let! vs = FlUtils.snapshot this.driver
                let actStr = Actions.actionToString cc.action
                let t = this.acceptVisualState vs
                let t = t.prependAction actStr
                return t
            }
