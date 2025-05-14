namespace rec FsOperator
open System
open System.Threading.Channels
open FsOpCore
open System.Net.Sockets

type CUAState = CUA_Init | CUA_Loop | CUA_Pause

type ChatMode = 
    | CM_Text of Chat
    | CM_Voice of {| connection: RTOpenAI.Api.Connection option; chat : Chat |}
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
    let postLog bus msg =  postMessage bus (ClientMsg.AppendLog msg) 
    let postAction bus action = postMessage bus (SetAction action) 
    let postWarning bus warning = postMessage bus (StatusMsg_Set warning)
    let postToCua bus req = bus.toCua.Writer.TryWrite(req) |> ignore


//State needed for the duration of a task
type RunState = {
    bus : Bus
    lastCuaResponse : Ref<FsResponses.Response option>
    question : string
    tokenSource : System.Threading.CancellationTokenSource
    instructions : Instructions
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

type BrowserAppState = {
    clientId    : string
    port        : int
    tokenSource : System.Threading.CancellationTokenSource
    outChannel  : Channel<P2PFromServer>
    pid         : int option
    listener    : TcpListener option
}
with static member Create() =
            {
                clientId = newId()
                port = P2p.defaultPort
                tokenSource = new System.Threading.CancellationTokenSource()
                outChannel = Channel.CreateBounded(10)
                pid = None
                listener = None
            }

//convenice functions to manage RunState
module RunState =
    

    let appendChatMsg msg (runState:RunState option) =
        runState
        |> Option.map (fun runState -> 
            let chatMode = 
                match runState.chatMode with
                | CM_Text msgs -> CM_Text (Chat.append msg msgs)
                | CM_Voice v -> CM_Voice {|v with chat = Chat.append msg v.chat |}
                | CM_Init -> CM_Init
            {runState with chatMode = chatMode})

    let setVoiceConnecton conn (rs:RunState option) = 
        rs 
        |> Option.map (fun rs ->
            match rs.chatMode with 
            | CM_Voice v -> CM_Voice {|v with connection = conn|}
            | x -> x 
            |> fun chatMode -> {rs with chatMode = chatMode})

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
        
    let lastAssistantMessage (runState:RunState option)  = 
        runState
        |> messages
        |> List.tryLast
        |> Option.bind (function | Assistant m -> Some m | _ -> None)
        
    let voiceConnection (runState:RunState option)  = 
        runState 
        |> Option.map (fun rs -> 
            match rs.chatMode with
            | CM_Voice v when v.connection.IsSome -> v.connection.Value
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
                        {|
                         connection = RTOpenAI.Api.Connection.create() |> Some
                         chat = Chat.Default                         
                        |}
        }
        
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
    initialized : bool
    instructions: Instructions
    mailbox : Channel<ClientMsg>
    log : string list
    url : string
    action : string
    statusMsg : (DateTime option*string)
    browserState : BrowserAppState
}

type ClientMsg =
    | Initialize
    | TextChat_StartStopTask

    | Browser_Connected of {|clientId:string; pid:int|}
    | Browser_Started of BrowserAppState option
    | Browser_SocketDisconnected 
    | Browser_ProcessExited
    | Browser_UrlSet of string
    | Error of exn

    | SetInstructions of string
    | AppendLog of string
    | ClearLog
    | SetUrl of string
    | SetAction of string
    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | TurnEnd
    | Abort of (exn option*string)
    | TestSomething

    | Chat_Append of ChatMsg
    | Chat_UpdateQuestion of string
    | Chat_HandleTurnEnd
    | Chat_Submit

    | VoicChat_StartStop
    | VoiceChat_RunInstructions of (string*string)


