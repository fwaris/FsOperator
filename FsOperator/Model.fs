namespace FsOperator
open System.Threading.Channels
open Microsoft.Playwright

type Model = {
    playwright:IPlaywright option
    instructions: string
    toModel : Channel<FsResponses.Request>
    fromModel : Channel<FsResponses.Response>
    tokenSource : System.Threading.CancellationTokenSource option
    log : string
}

type ClientMsg =
    | Initialize
    | Start
    | Stop
    | BrowserConnected of IPlaywright
    | SetInstructions of string
    | AppendLog of string
    | ClearLog

