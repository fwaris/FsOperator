namespace FsOpCore
open FsResponses
open Microsoft.SemanticKernel

///reuseable state needed to keep track of a running task
type TaskState<'inMsg,'outMsg> = {
        id              : string
        target          : string
        cuaMessages     : ChatMsg list
        cuaPrompt       : string
        reasonerItems   : IOitem list
        reasonerPrevId  : string option
        reasonerPrompt  : string option
        driver          : IUIDriver
        actions         : string list
        kernel          : Kernel
        bus             : WBus<'inMsg,'outMsg>
        usage           : Map<string,FsResponses.Usage list>
        toolDefs        : Tool list
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
                               usage = Map.empty
                               toolDefs = tools
                            }


        member this.prependCuaMessage msg = {this with cuaMessages = msg::this.cuaMessages}
        member this.prependReasonerItems items = {this with reasonerItems = items}
        member this.setPrevId id = {this with reasonerPrevId = Some id}        
        member this.prependAction a = {this with actions = a::this.actions |> List.truncate C.MAX_ACTIONS }

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
       