namespace FsOperator
open FsOpCore
open System
open System.Threading
open System.Threading.Channels
open FSharp.Control

type WErrorType = WE_Responses of string | Other of string

type W_Msg<'t> = 
    | W_Cua of FsResponses.Response
    | W_App of 't
    | W_Voice of RTOpenAI.Api.Events.ServerEvent
    | W_Reasoner of FsResponses.Response
    | W_Errr of WErrorType

type WBus<'appIn,'appOut> = 
    {
        inCh  : Channel<W_Msg<'appIn>>
        outCh : Channel<'appOut>
    }
    with 
        static member Create<'appIn,'appOut>() = 
            {
                inCh  = Channel.CreateBounded<W_Msg<'appIn>>(10)
                outCh = Channel.CreateBounded<'appOut>(10)
            }

///A type that represents a state where 'state' is a function that takes an event and returns 
///the next state + a list output events
type F<'Event,'OutEvent> = F of ('Event -> Async<F<'Event,'OutEvent>>)*'OutEvent list

module Workflow =

    ///accepts current state and input event,
    ///returns nextState and publishes any output events
    let private transition bus state event = async {
        let! (F(nextState,outEvents)) = state event
        outEvents |> List.iter (bus.outCh.Writer.TryWrite>>ignore)
        return nextState
    }

    let run (cts:CancellationTokenSource) bus initState =
        let runner =  
            bus.inCh.Reader.ReadAllAsync(cts.Token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.scanAsync (transition bus) initState
            |> AsyncSeq.iter (fun x -> ())

        let catcher = 
            async {
                match! Async.Catch runner with 
                | Choice1Of2 _ -> Log.info $"Workflow done"
                | Choice2Of2 exn -> Log.exn(exn,"Workflow.run")
                
            }

        Async.Start(catcher,cts.Token)
