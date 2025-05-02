namespace rec FsOperator
open System
open System.Threading.Channels
open Microsoft.Playwright
open WebViewControl

//need these stable for the duration of the run
type RunState = {
    toModel : Channel<FsResponses.Request>
    fromModel : Channel<FsResponses.Response>
    lastResponse : Ref<FsResponses.Response option>
    tokenSource : System.Threading.CancellationTokenSource
    browser : IBrowser
    mailbox : Channel<ClientMsg>
    instructions : string
}
with static member Create browser mailbox instructions =
            {
                toModel = Channel.CreateBounded(10)
                fromModel = Channel.CreateBounded(10)
                lastResponse = ref None
                tokenSource = new System.Threading.CancellationTokenSource()
                browser = browser
                mailbox = mailbox
                instructions = instructions
            }

type Model = {
    runState : RunState option
    instructions: string
    mailbox : Channel<ClientMsg>
    log : string list
    output : string
    url : Uri
    browser : IBrowser option
    webview : Ref<WebView option>
    action : string
    warning : string
}

type ClientMsg =
    | Initialize
    | Start
    | Stop
    | BrowserConnected of IBrowser
    | SetInstructions of string
    | AppendLog of string
    | ClearLog
    | AppendOutput of string
    | ClearOutput
    | SetUrl of string
    | SetAction of string
    | SetWarning of string
    | TurnEnd


