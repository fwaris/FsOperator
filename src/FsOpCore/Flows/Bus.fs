namespace FsOpCore
open System
open System.Threading.Channels
open System.Threading
open FSharp.Control
(*
type Subscription<'T> =
    { Reader: ChannelReader<'T>
      Complete: unit -> unit }
    { Reader = channel.Reader
      Complete = fun () ->
        subscribers.TryRemove(name) |> ignore
        channel.Writer.Complete() }
*)

type PubSub<'T>(cancellationToken:CancellationToken) =
    let central = Channel.CreateBounded<'T>(C.MAX_BUS_QUEUE_DEPTH)
    let subscribers = System.Collections.Concurrent.ConcurrentDictionary<string, Channel<'T>>()
    
    do
        let comp = 
            central.Reader.ReadAllAsync(cancellationToken)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync(fun m -> async {
                for kvp in subscribers do
                    let r = kvp.Value.Writer.TryWrite(m)
                    if not r then
                        Log.info $"Dropped msg {m} to {kvp.Key}"
            })
        async {
            match! Async.Catch(comp) with
            | Choice1Of2 _ -> Log.info "Bus stopped"
            | Choice2Of2 ex -> Log.exn(ex,"Error message dispatch for bus")
            for kvp in subscribers.Values do
                kvp.Writer.Complete()
        }
        |> Async.Start
    
    /// Publishes a message to all subscribers
    member _.Publish(msg: 'T) =
        central.Writer.TryWrite(msg) |> ignore

    /// Subscribe and receive messages; returns a Subscription
    member _.Subscribe(name:string) =
        if subscribers.ContainsKey name then
            failwith $"{name} is already registered in bus"
        let channel = Channel.CreateBounded<'T>(C.MAX_BUS_QUEUE_DEPTH)
        subscribers[name] <- channel
        channel

(*

// Example usage:
let cts = new Threading.CancellationTokenSource()
let bus = PubSub<string>()
bus.StartDispatching(cts.Token)

let sub1 = bus.Subscribe()
let sub2 = bus.Subscribe()

let printSubscriber name sub =
    Task.Run(fun _ ->
        task {
            while let! has = sub.Reader.WaitToReadAsync() in has do
                let! item = sub.Reader.ReadAsync()
                printfn $"%s{name} received: %s{item}"
        })

printSubscriber "A" sub1 |> ignore
printSubscriber "B" sub2 |> ignore

bus.Publish "Hello"
bus.Publish "World"

Threading.Thread.Sleep(500)
cts.Cancel()
*)