namespace rec FsOperator
open System
open System.Threading.Channels
open FsOpCore
open System.Net.Sockets

type CUAState = CUA_Init | CUA_Loop | CUA_Pause

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

module Bus =
    let postMessage (bus:Bus) msg = bus.mailbox.Writer.TryWrite(msg) |> ignore
    let postLog bus msg =  postMessage bus (ClientMsg.Log_Append msg) 
    let postAction bus action = postMessage bus (Action_Set action) 
    let postWarning bus warning = postMessage bus (StatusMsg_Set warning)
    let postToCua bus req = bus.toCua.Writer.TryWrite(req) |> ignore


//State needed for the duration of a task
type RunState = {
    bus : Bus
    lastCuaResponse : Ref<FsResponses.Response option>
    question : string
    tokenSource : System.Threading.CancellationTokenSource
    instructions : OpTask
    cuaState : CUAState
    chatMode : ChatMode
    lastFunctionCallId : Ref<string option> 
}
with static member Create mailbox instructions =
            {
                bus = Bus.Create mailbox
                lastCuaResponse = ref None
                question = ""
                tokenSource = new System.Threading.CancellationTokenSource()
                instructions = instructions
                cuaState = CUA_Init
                chatMode = CM_Text Chat.Default
                lastFunctionCallId = ref None
            }



type BST = BST_Init | BST_Ready | BST_AwaitAck
type BrowserAppState = {
    port        : int
    tokenSource : System.Threading.CancellationTokenSource
    outChannel  : Channel<P2PFromServer>
    pid         : int option
    listener    : TcpListener option
    state       : BST

}
with static member Create() =
            {
                port = P2p.defaultPort
                tokenSource = new System.Threading.CancellationTokenSource()
                outChannel = Channel.CreateBounded(10)
                pid = None
                listener = None
                state = BST_Init
            }


type BrowserMode = External of {|pid:int option|} | Embedded of BrowserAppState

module BrowserMode =
    let isEmbedded = function | Embedded _ -> true | _ -> false
    let isExternal = function | External _ -> true | _ -> false
    let pid = function | External p -> p.pid | Embedded b -> b.pid
    let port = function | External p -> P2p.defaultPort | Embedded b -> b.port
    let setPid pid = function | External p -> External {|pid = Some pid|} | Embedded b -> Embedded {b with pid= Some pid}
    let setEmbState state = function | External p -> External p | Embedded b -> Embedded {b with state=state}
    let setEmbAppState bst = function | External p -> External p | Embedded b -> Embedded bst

    let postUrl url = function
        | External p -> Browser.goToPage url |> Async.Start
        | Embedded b -> b.outChannel.Writer.TryWrite (P2PFromServer.Server_SetUrl url ) |> ignore

    let isReady browserMode =
        match browserMode with
        | External p -> p.pid.IsSome
        | Embedded b -> b.state = BST_Ready


//convenice functions to manage RunState
module RunState =

    let appendChatMsg msg (runState:RunState option) =
        runState
        |> Option.map (fun runState -> 
            let chatMode = 
                match runState.chatMode with
                | CM_Text msgs -> CM_Text (Chat.append msg msgs)
                | CM_Voice v -> CM_Voice {v with chat = Chat.append msg v.chat }
                | CM_Init -> CM_Init
            {runState with chatMode = chatMode})

    let setVoiceConnecton conn (rs:RunState option) = 
        rs 
        |> Option.map (fun rs ->
            match rs.chatMode with 
            | CM_Voice v -> CM_Voice {v with connection = conn}
            | x -> x 
            |> fun chatMode -> {rs with chatMode = chatMode})

    let chatMode = function | Some (rs:RunState) -> rs.chatMode | _ -> CM_Init
    let setMode mode (runState:RunState option)  = runState |> Option.map (fun rs -> {rs with chatMode=mode})
    let setState state (runState:RunState option)  = runState |> Option.map (fun rs -> {rs with cuaState=state})
    let lastFunctionCallId (runState:RunState option)  = runState |> Option.bind (fun rs -> rs.lastFunctionCallId.Value)
    let setQuestion question = function | Some (rs:RunState) -> Some {rs with question=question} | _ -> None
    let question (runState:RunState option)  = runState |> Option.map _.question |> Option.defaultValue ""
    let lastCuaResponse (runState:RunState option)  = runState |> Option.bind (fun rs -> rs.lastCuaResponse.Value)

    let messages (runState:RunState option)  = 
        runState 
        |> Option.map (fun rs -> 
            match rs.chatMode with
            | CM_Text c -> c.messages
            | CM_Voice v -> v.chat.messages 
            | CM_Init -> [])
        |> Option.defaultValue []

    let textChatMessages (runState:RunState option)  = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Text c -> Some c.messages
            | _ -> None)
        |> Option.defaultValue []

    let voiceChatMessages (runState:RunState option)  = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> Some v.chat.messages
            | _ -> None)
        |> Option.defaultValue []


    let lastAssistantMessage (runState:RunState option)  = 
        runState
        |> messages
        |> List.tryLast
        |> Option.bind (function | Assistant m -> Some m | _ -> None)
        
    let voiceConnection (runState:RunState option)  = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v when v.connection.Value.IsSome  -> v.connection.Value
            | CM_Voice v -> failwith "no voice connection set"
            | _ -> failwith "not a voice connection")

    let initForText mailbox instructions = 
        {RunState.Create mailbox instructions with 
            chatMode = CM_Text Chat.Default
            cuaState = CUA_Init
        }

    let initForVoice mailbox instructions = 
        {RunState.Create mailbox instructions with             
            chatMode = CM_Voice 
                        {
                         connection = ref (RTOpenAI.Api.Connection.create() |> Some)
                         chat = Chat.Default
                         voiceAsstInstructions = instructions.voiceAsstInstructions
                        }
        }
    
    let voiceAsstInstructions (runState:RunState option) = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> Some v.voiceAsstInstructions
            | _ -> None)
        |> Option.defaultValue ""

    let setVoiceAsstInstructions text (runState:RunState option) = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> 
                let mode = CM_Voice {v with voiceAsstInstructions = text}
                Some {rs with chatMode = mode}
            | _ -> None)

    let voiceSysMsg (runState:RunState option) = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> v.chat.systemMessage
            | _ -> None)
        |> Option.defaultValue ""

    let setVoiceSysMsg text (runState:RunState option) = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> 
                let mode = CM_Voice {v with chat = Chat.setSystemMessage text v.chat}
                Some {rs with chatMode = mode}
            | _ -> None)                

    let appendVoiceMessage text (runState:RunState option) =
        match runState with 
        | Some rs -> 
            match rs.chatMode with 
            | CM_Voice v -> 
                if v.chat.systemMessage.IsNone then 
                   Some {rs with chatMode = CM_Voice {v with chat.systemMessage = Some text}}
                else 
                    RunState.appendChatMsg (User text) runState 
            | _ -> runState
        | None -> None

    let lastResponseComputerCall (runState:RunState option) = 
        runState 
        |> Option.bind (fun rs -> 
            rs.lastCuaResponse.Value
            |> Option.bind (fun r -> 
                r.output |> List.tryPick (function FsResponses.Computer_call cb -> Some cb | _ -> None)))
        
    let stop (runState:RunState option ) =
        match runState with
        | Some runState -> 
            runState.tokenSource.Cancel()
            runState.bus.fromCua.Writer.TryComplete() |> ignore
            runState.bus.toCua.Writer.TryComplete()   |> ignore
            Some {runState with cuaState = CUA_Init}
        | None -> None
 
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

type Model = {
    runState : RunState option
    opTask: OpTask
    isDirty : bool
    mailbox : Channel<ClientMsg>
    log : string list
    action : string
    statusMsg : (DateTime option*string)
    browserMode : BrowserMode
    isFlashing : bool
}

type ClientMsg =
    | Initialize
    | InitializeDevMode
    | InitializeExternalBrowser

    | Browser_Connected of {|pid:int|}
    | Browser_Emb_Started of BrowserAppState option
    | Browser_Emb_SocketDisconnected 
    | Browser_Emb_ProcessExited
    | Browser_Emb_UrlSet of string

    | OpTask_SetTextInstructions of string
    | OpTask_Update of OpTask
    | OpTask_SetUrl of string
    | OpTask_MarkDirty of bool
    | OpTask_Load
    | OpTask_Loaded of OpTask
    | OpTask_Save

    | Action_Set of string
    | Action_Flash of bool

    | Log_Append of string
    | Log_Clear

    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | Error of exn
    | Abort of (exn option*string)
    | TestSomething

    | Chat_CUATurnEnd
    | Chat_Append of ChatMsg
    | Chat_UpdateQuestion of string
    | Chat_HandleTurnEnd
    | Chat_Resume

    | TextChat_StartStopTask
    | VoicChat_StartStop
    | VoiceChat_RunInstructions of (string*string)


