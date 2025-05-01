namespace FsOperator
open System
open System.Threading.Channels
open Microsoft.Playwright
open AvaloniaWebView

type Model = {
    playwright:IPlaywright option
    instructions: string
    toModel : Channel<FsResponses.Request>
    fromModel : Channel<FsResponses.Response>
    tokenSource : System.Threading.CancellationTokenSource option
    log : string
    output : string
    url : Uri
    webview : Ref<WebView option>
}

type ClientMsg =
    | Initialize
    | Start
    | Stop
    | BrowserConnected of IPlaywright
    | SetInstructions of string
    | AppendLog of string
    | ClearLog
    | AppendOutput of string
    | ClearOutput
    | SetUrl of string


