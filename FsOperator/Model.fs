namespace rec FsOperator
open System
open System.Threading.Channels
open WebViewControl

type ChatState = CS_Init | CS_Loop | CS_Prompt

type ChatMode = 
    | CM_Text of ChatMsg list
    | CM_Voice of {| connecton : RTOpenAI.Api.Connection option; initialInstructions : string option; chatHistory : ChatMsg list |}
    | CM_Init

//State needed for the duration of a task
type RunState = {
    toModel : Channel<FsResponses.Request>
    fromModel : Channel<FsResponses.Response>
    lastCuaResponse : Ref<FsResponses.Response option>
    question : string
    tokenSource : System.Threading.CancellationTokenSource
    mailbox : Channel<ClientMsg>
    instructions : Instructions
    chatState : ChatState
    chatMode : ChatMode
    lastFunctionCallId : Ref<string option> 
}
with static member Create mailbox instructions =
            {
                toModel = Channel.CreateBounded(10)
                fromModel = Channel.CreateBounded(10)
                lastCuaResponse = ref None
                question = ""
                tokenSource = new System.Threading.CancellationTokenSource()
                mailbox = mailbox
                instructions = instructions
                chatState = CS_Init
                chatMode = CM_Text []
                lastFunctionCallId = ref None
            }

//convenice functions to manage RunState
module RunState =

    let appendChatMsg msg (runState:RunState option) =
        runState
        |> Option.map (fun runState -> 
            let chatMode = 
                match runState.chatMode with
                | CM_Text msgs -> CM_Text (Chat.append msg msgs)
                | CM_Voice v -> CM_Voice {|v with chatHistory = Chat.append msg v.chatHistory |}
                | CM_Init -> CM_Init
            {runState with chatMode = chatMode})

    let setVoiceConnecton conn (rs:RunState option) = 
        rs 
        |> Option.map (fun rs ->
            match rs.chatMode with 
            | CM_Voice v -> CM_Voice {|v with connecton = conn|}
            | x -> x 
            |> fun chatMode -> {rs with chatMode = chatMode})

    let setMode mode (runState:RunState option)  = runState |> Option.map (fun rs -> {rs with chatMode=mode})
    let setState state (runState:RunState option)  = runState |> Option.map (fun rs -> {rs with chatState=state})
    let lastFunctionCallId (runState:RunState option)  = runState |> Option.bind (fun rs -> rs.lastFunctionCallId.Value)
    let setQuestion question = function | Some rs -> Some {rs with question=question} | _ -> None
    let lastCuaResponse (runState:RunState option)  = runState |> Option.bind (fun rs -> rs.lastCuaResponse.Value)

    let lastAssistantMessage (runState:RunState option)  = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Text msgs -> msgs |> List.tryLast
            | CM_Voice v -> v.chatHistory |> List.tryLast
            | CM_Init -> None)
        |> Option.bind (function | Assistant m -> Some m | _ -> None)
        
    let voiceConnection (runState:RunState option)  = 
        runState 
        |> Option.bind (fun rs -> 
            match rs.chatMode with
            | CM_Voice v -> v.connecton
            | _ -> None)

    let initForText mailbox instructions = 
        {RunState.Create mailbox instructions with
            chatState = CS_Init
            chatMode = CM_Text []
            lastFunctionCallId = ref None
        }

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
    instructions: string
    mailbox : Channel<ClientMsg>
    log : string list
    output : string
    url : string
    webview : Ref<WebView option>
    action : string
    statusMsg : (DateTime option*string)
}

type ClientMsg =
    | Initialize
    | TextChat_StartStopTask
    | StopIfRunning
    | BrowserConnected
    | SetInstructions of string
    | AppendLog of string
    | ClearLog
    | SetUrl of string
    | SetAction of string
    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | TurnEnd
    | TerminateWithException of exn
    | TestSomething
    | Chat_Append of ChatMsg
    | Chat_UpdateQuestion of string
    | Chat_Clear
    | Chat_HandleTurnEnd
    | Chat_Submit
    | VoicChat_StartStop
    | VoiceChat_RunInstructions of (string*string)

