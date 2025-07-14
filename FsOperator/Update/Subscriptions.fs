namespace FsOperator
open System
open System.Threading.Channels
open FSharp.Control

module Subscriptions = 

    let subscribeMailbox<'model,'msg> (mailbox:Channel<'msg>) (model:'model)=
        let backgroundEvent dispatch =
            let ctx = new System.Threading.CancellationTokenSource()
            let comp =
                async{
                    let comp =
                            mailbox.Reader.ReadAllAsync()
                            |> AsyncSeq.ofAsyncEnum
                            |> AsyncSeq.iter dispatch
                    match! Async.Catch(comp) with
                    | Choice1Of2 _ -> printfn "dispose subscribeBackground"
                    | Choice2Of2 ex -> printfn "%s" ex.Message
                }
            Async.Start(comp,ctx.Token)
            {new IDisposable with member _.Dispose() = ctx.Dispose(); printfn "disposing subscription backgroundEvent";}
        backgroundEvent

    let subscription mailbox model =

        let sub2 = subscribeMailbox mailbox model
        [
            [nameof sub2], sub2
        ]

