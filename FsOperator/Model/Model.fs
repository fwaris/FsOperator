namespace rec FsOperator
open System
open System.Threading.Channels
open FsOpCore

///computer use assistant states
type CUAState =
    ///initial (or ready start)
    | CUA_Init
    ///wait for the target process (browser, etc. to become available)
    | CUA_Await_Target
    ///cua model is requestion computer actions
    | CUA_Loop
    ///the cua model has been asked to stop computer actions and produce the results
    | CUA_Loop_Closing
    //the cua model is waiting for new input
    | CUA_Pause

type VoiceChatState = {
    connection: Ref<RTOpenAI.Api.Connection option>
    chat : Chat
    voiceAsstInstructions : string
}

type ChatMode =
    | CM_Text of Chat
    | CM_Voice of VoiceChatState
    | CM_Init

type Bus = {
    toCua : Channel<FsResponses.Request>
    fromCua : Channel<FsResponses.Response>
    mailbox : Channel<ClientMsg>
}
with static member Create mailbox =
                {
                    toCua = Channel.CreateBounded(10)
                    fromCua = Channel.CreateBounded(10)
                    mailbox = mailbox
                }

type FlowState = 
    | FL_Init 
    | FL_Flow of {| flow : IFlow<TaskFlow.TaskFLowMsgIn>; chat:Chat |}
    with 
        member this.setChat ch = match this with FL_Flow fs -> FL_Flow {|fs with chat=ch|} | f -> f
        member this.messages() = match this with FL_Flow fs -> fs.chat.messages | _ -> []

module Bus =
    let postMessage (bus:Bus) msg = bus.mailbox.Writer.TryWrite(msg) |> ignore
    let postLog bus msg =  postMessage bus (ClientMsg.Log_Append msg)
    let postAction bus action = postMessage bus (Action_Set action)
    let postWarning bus warning = postMessage bus (StatusMsg_Set warning)
    let postToCua bus req = bus.toCua.Writer.TryWrite(req) |> ignore

///State needed for the duration of a running task
type TaskState = {
    bus : Bus
    lastCuaResponse : Ref<FsResponses.Response option>
    screenshots : Ref<string list>
    question : string
    tokenSource : System.Threading.CancellationTokenSource
    instructions : OpTask
    cuaState : CUAState
    chatMode : ChatMode
    lastFunctionIdMap : Ref<Map<string,string>>
}
with static member Create mailbox instructions =
            {
                bus = Bus.Create mailbox
                lastCuaResponse = ref None
                screenshots = ref []
                question = ""
                tokenSource = new System.Threading.CancellationTokenSource()
                instructions = instructions
                cuaState = CUA_Init
                chatMode = CM_Text Chat.Default
                lastFunctionIdMap = ref Map.empty
            }

//convenice functions to manage task state
module TaskState =

    let functionId (key:string) (taskState:TaskState) =
        taskState.lastFunctionIdMap.Value |> Map.tryFind key

    let setFunctionId (key:string) (id:string) (ts:TaskState) =
        let m = ts.lastFunctionIdMap.Value |> Map.add key id
        ts.lastFunctionIdMap.Value <- m

    let removeFunctionId (key:string) (ts:TaskState) =
        let m = ts.lastFunctionIdMap.Value |> Map.remove key
        ts.lastFunctionIdMap.Value <- m

    let appendChatMsg msg (taskState:TaskState option) =
        taskState
        |> Option.map (fun runState ->
            let chatMode =
                match runState.chatMode with
                | CM_Text msgs -> CM_Text (Chat.append msg msgs)
                | CM_Voice v -> CM_Voice {v with chat = Chat.append msg v.chat }
                | CM_Init -> CM_Init
            {runState with chatMode = chatMode})

    let setVoiceConnecton conn (taskState:TaskState option) =
        taskState
        |> Option.map (fun rs ->
            match rs.chatMode with
            | CM_Voice v -> CM_Voice {v with connection = conn}
            | x -> x
            |> fun chatMode -> {rs with chatMode = chatMode})

    let chatMode = function | Some (taskState:TaskState) -> taskState.chatMode | _ -> CM_Init
    let cuaMode = function | Some (taskState:TaskState) -> taskState.cuaState | _ -> CUA_Init
    let setMode mode (taskState:TaskState option)  = taskState |> Option.map (fun ts -> {ts with chatMode=mode})
    let setCuaMode state (taskState:TaskState option)  = taskState |> Option.map (fun ts -> {ts with cuaState=state})
    let setQuestion question = function | Some (rs:TaskState) -> Some {rs with question=question} | _ -> None
    let question (taskState:TaskState option)  = taskState |> Option.map _.question |> Option.defaultValue ""
    let lastCuaResponse (taskState:TaskState option)  = taskState |> Option.bind (fun rs -> rs.lastCuaResponse.Value)

    let messages (taskState:TaskState option)  =
        taskState
        |> Option.map (fun rs ->
            match rs.chatMode with
            | CM_Text c -> c.messages
            | CM_Voice v -> v.chat.messages
            | CM_Init -> [])
        |> Option.defaultValue []

    let textChatMessages (taskState:TaskState option)  =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Text c -> Some c.messages
            | _ -> None)
        |> Option.defaultValue []

    let voiceChatMessages (taskState:TaskState option)  =
        taskState
        |> Option.bind (fun ts ->
            match ts.chatMode with
            | CM_Voice v -> Some v.chat.messages
            | _ -> None)
        |> Option.defaultValue []


    let lastAssistantMessage (taskState:TaskState option)  =
        taskState
        |> messages
        |> List.tryLast
        |> Option.bind (function | Assistant m -> Some m | _ -> None)

    let voiceConnection (taskState:TaskState option)  =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Voice v  -> Some v.connection
            | _ -> None)
        |> Option.defaultWith (fun _ -> failwith "not a voice connection")

    let initForText mailbox opTask =
        {TaskState.Create mailbox opTask with
            chatMode = CM_Text {Chat.Default with systemMessage = Some opTask.textModeInstructions}
            cuaState = CUA_Init
        }

    let initForVoice mailbox instructions =
        {TaskState.Create mailbox instructions with
            chatMode = CM_Voice
                        {
                         connection = ref None
                         chat = Chat.Default
                         voiceAsstInstructions = OpTask.voicePromptOrDefault instructions.voiceAsstInstructions
                        }
        }

    let voiceAsstInstructions (taskState:TaskState option) =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Voice v -> Some v.voiceAsstInstructions
            | _ -> None)
        |> Option.defaultValue ""

    let setVoiceAsstInstructions text (taskState:TaskState option) =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Voice v ->
                let mode = CM_Voice {v with voiceAsstInstructions = text}
                Some {rs with chatMode = mode}
            | _ -> None)

    let voiceSysMsg (taskState:TaskState option) =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Voice v -> v.chat.systemMessage
            | _ -> None)
        |> Option.defaultValue ""

    let setVoiceSysMsg text (taskState:TaskState option) =
        taskState
        |> Option.bind (fun rs ->
            match rs.chatMode with
            | CM_Voice v ->
                let mode = CM_Voice {v with chat = Chat.setSystemMessage text v.chat}
                Some {rs with chatMode = mode}
            | _ -> None)

    let appendVoiceMessage text (taskState:TaskState option) =
        match taskState with
        | Some rs ->
            match rs.chatMode with
            | CM_Voice v ->
                if v.chat.systemMessage.IsNone then
                   Some {rs with chatMode = CM_Voice {v with chat.systemMessage = Some text}}
                else
                    TaskState.appendChatMsg (User text) taskState
            | _ -> taskState
        | None -> None

    let lastResponseComputerCall (taskState:TaskState option) =
        taskState
        |> Option.bind (fun rs ->
            rs.lastCuaResponse.Value
            |> Option.bind (fun r ->
                r.output |> List.tryPick (function FsResponses.Computer_call cb -> Some cb | _ -> None)))

    let stop (taskState:TaskState option ) =
        match taskState with
        | Some ts ->
            ts.tokenSource.Cancel()
            ts.bus.fromCua.Writer.TryComplete() |> ignore
            ts.bus.toCua.Writer.TryComplete()   |> ignore
            Some {ts with cuaState = CUA_Init}
        | None -> None

    let appendScreenshot (screenshot:string) (taskState:TaskState option ) =
        match taskState with
        | Some ts ->
            ts.screenshots.Value <- (screenshot :: ts.screenshots.Value) |> List.truncate 3
        | _ -> ()

    let screenshots (taskState: TaskState option) =
        match taskState with
        | Some ts -> ts.screenshots.Value |> List.rev
        | None -> []

//request to get the ephemeral key
type KeyReq = {
    model : string
    modalities : string list
    instructions : string
}
with static member Default = {
        model = ""
        modalities = ["audio"; "text"]
        instructions = "You are a friendly assistant"
    }

type BrowserMode = BM_Init | BM_Ready

type Model = {
    ui          : UserInterface
    driver      : IUIDriver
    taskState   : TaskState option
    opTask      : OpTask
    isDirty     : bool
    mailbox     : Channel<ClientMsg>
    log         : string list
    action      : string
    statusMsg   : (DateTime option*string)
    browserMode : BrowserMode
    isFlashing  : bool
    flow        : FlowState
}
    with member this.post msg = this.mailbox.Writer.TryWrite msg |> ignore

type ClientMsg =
    | InitializeExternalBrowser
    | Browser_Connected of unit

    | OpTask_SetTextInstructions of string
    | OpTask_Update of OpTask
    | OpTask_SetTarget of string
    | OpTask_MarkDirty
    | OpTask_ClearDirty
    | OpTask_Load
    | OpTask_LoadSample of OpTask
    | OpTask_Loaded of OpTask option
    | OpTask_Save
    | OpTask_SaveAs
    | OpTask_Clear
    | OpTask_Saved of OpTask option

    | Flow_Start
    | Flow_Msg of TaskFlow.TaskFLowMsgOut

    | Action_Set of string
    | Action_Flash of bool

    | Log_Append of string
    | Log_Clear

    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | SyncUrlToBrowser
    | Error of exn
    | Abort of (exn option*string)
    | Nop of unit
    | TestSomething

    | Chat_CUATurnEnd
    | Chat_Append of ChatMsg
    | Chat_UpdateQuestion of string
    | Chat_HandleTurnEnd
    | Chat_Resume
    | Chat_StopAndSummarize
    | Chat_GotSummary_Cua of (string*string)
    | Chat_GotSummary_Alt of (string*string)
    | Chat_TargetAcquired

    | TextChat_StartStopTask
    | VoiceChat_StartStop
    | VoiceChat_RunInstructions of (string*string)


