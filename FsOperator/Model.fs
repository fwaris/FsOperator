namespace rec FsOperator
open System
open System.Threading.Channels
open PuppeteerSharp
open WebViewControl


type ChatState = CS_Init | CS_Loop | CS_Prompt

//need these stable for the duration of the run
type RunState = {
    toModel : Channel<FsResponses.Request>
    fromModel : Channel<FsResponses.Response>
    lastResponse : Ref<FsResponses.Response option>
    tokenSource : System.Threading.CancellationTokenSource
    mailbox : Channel<ClientMsg>
    instructions : string
    chatHistory : ChatMsg list
    chatState : ChatState
}

with static member Create mailbox instructions =
            {
                toModel = Channel.CreateBounded(10)
                fromModel = Channel.CreateBounded(10)
                lastResponse = ref None
                tokenSource = new System.Threading.CancellationTokenSource()
                mailbox = mailbox
                instructions = instructions
                chatHistory = [Placeholder]
                chatState = CS_Init
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
    | Start
    | Stop
    | BrowserConnected
    | SetInstructions of string
    | AppendLog of string
    | ClearLog
    | SetUrl of string
    | SetAction of string
    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | TurnEnd
    | StopWithError of exn
    | TestSomething
    | Chat_Append of ChatMsg
    | Chat_UpdateQuestion of string
    | Chat_Clear
    | Chat_Respond
    | Chat_Submit


