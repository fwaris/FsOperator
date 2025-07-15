namespace FsOpCoreUI
open System
open System.Threading.Channels   
open FSharp.Control
open FsOpCore
open Elmish

module Subscriptions =     

    let subscribe<'msg> (name:string) (hookPoster:('msg->unit) -> unit)  =
        let backgroundEvent dispatch =
            let mailbox = Channel.CreateBounded<'msg>(10)
            let post msg = mailbox.Writer.TryWrite msg |> ignore
            hookPoster post
            let ctx = new System.Threading.CancellationTokenSource()
            let comp =
                async{
                    let comp =
                            mailbox.Reader.ReadAllAsync(ctx.Token)
                            |> AsyncSeq.ofAsyncEnum
                            |> AsyncSeq.iter dispatch
                    match! Async.Catch(comp) with
                    | Choice1Of2 _ -> printfn $"dispose subscribeBackground {name}"
                    | Choice2Of2 ex -> Log.exn(ex,$"subscription {name}")
                }
            Async.Start(comp,ctx.Token)
            {new IDisposable with 
                member _.Dispose() = 
                    ctx.Dispose()
                    mailbox.Writer.TryComplete() |> ignore
                    printfn $"disposing subscription {name}";
            }
        backgroundEvent

    let create<'msg> name post : Sub<'msg>=
        let sub = subscribe name post
        [
            [name], sub
        ]

