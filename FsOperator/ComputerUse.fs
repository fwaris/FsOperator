namespace FsOperator
open FSharp.Control
open System.Threading
open System.Threading.Channels
open Microsoft.Playwright
open FsResponses

module ComputerUse =
  
    let start (ctx:CancellationTokenSource) (fromModel:Channel<Response>) (toModel:Channel<Request>) =
        let sendLoop = 
            toModel.Reader.ReadAllAsync()
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun request ->
                async {
                    let! response = Api.create request (Api.defaultClient()) |> Async.AwaitTask
                    do! fromModel.Writer.WriteAsync(response).AsTask() |> Async.AwaitTask
                }
            )
        Async.Start(sendLoop, ctx.Token)

