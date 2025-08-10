namespace FsOpCore
open System
open Microsoft.SemanticKernel
open System.Threading
open System.Threading.Channels
open FSharp.Control
open FsResponses

type IFlow<'inMsg> = 
    abstract member Post : 'inMsg -> unit
    abstract member Terminate : unit -> unit

type WErrorType = WE_Error of string | WE_Exn of exn

type W_Msg_In<'input> = 
    | W_Msg of 'input
    | W_Err of WErrorType

    with 
        member this.msgType = 
            match this with 
            | W_Msg t -> $"W_App {t}"
            | W_Err e -> $"W_Error {e}" 
            

type WBus<'input,'output> = 
    {
        ///Channel for messages going into the flow.
        ///Use PostToFlow function instead of directly using this property, for consistent logging
        _flowChannel  : Channel<W_Msg_In<'input>>

        ///Channel to send messages to non-flow actors; the app and zero or more agents
        agentChannel  : Channel<'output>
    }
    with 
        static member QUEUE_MAX = 20
        static member Create<'appIn,'appOut>() = 
            let inOpts = BoundedChannelOptions(WBus<_,_>.QUEUE_MAX, SingleReader=true, SingleWriter=false)
            let outOpts = BoundedChannelOptions(WBus<_,_>.QUEUE_MAX, SingleReader=false, SingleWriter=false)
            {
                _flowChannel  = Channel.CreateBounded<W_Msg_In<'appIn>>(inOpts)
                agentChannel = Channel.CreateBounded<'output>(outOpts)
            }
        member this.Close() = 
            this._flowChannel.Writer.TryComplete() |> ignore
            this.agentChannel.Writer.TryComplete() |> ignore
        member this.PostToFlow msg = 
            match this._flowChannel.Writer.TryWrite msg with 
            | false -> Log.warn $"Bus dropped message {msg}"
            | true  -> ()
        member this.PostToAgent msg = 
            match this.agentChannel.Writer.TryWrite msg with 
            | false -> Log.warn $"Bus dropped message {msg}"
            | true  -> ()


///A type that represents a state where 'state' is a function that takes an event and returns 
///the next state + a list output events
type F<'Event,'OutEvent> = F of ('Event -> Async<F<'Event,'OutEvent>>)*'OutEvent list

module Workflow =   
    ///accepts current state and input event,
    ///returns nextState and publishes any output events
    let private transition (bus:WBus<_,'output>) state event = async {
        let! (F(nextState,outEvents)) = state event
        outEvents |> List.iter bus.PostToAgent
        return nextState
    }

    let run (token:CancellationToken) bus initState =
        let runner =  
            bus._flowChannel.Reader.ReadAllAsync(token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.map(fun m -> Log.info $"Workflow message: {m.msgType}"; m)
            |> AsyncSeq.scanAsync (transition bus) initState
            |> AsyncSeq.iter (fun x -> ())

        let catcher = 
            async {
                match! Async.Catch runner with 
                | Choice1Of2 _   -> Log.info $"Workflow done"
                | Choice2Of2 exn -> (WE_Exn >> W_Err >> bus.PostToFlow) exn
                                    Log.exn(exn,"Workflow.run")                
            }

        Async.Start(catcher,token)
