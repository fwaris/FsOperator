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

type WErrorType = WE_Responses of string | Other of string | WE_Exn of exn

type W_Msg<'t> = 
    | W_Cua of FsResponses.Response
    | W_App of 't
    | W_Voice of RTOpenAI.Api.Events.ServerEvent
    | W_Err of WErrorType

    ///Reasoner responses should have the correlation id attached for request-response pairing;
    ///a there may be multiple concurrent requests in play.
    ///Use Workflow.ReasonerMsgWithCorrId function to construct this union case from the raw response.
    | W_Reasoner of (string*FsResponses.Response) 

    with 
        member this.msgType = 
            match this with 
            | W_Cua _ -> "W_Cua"
            | W_App t -> $"W_App {t}"
            | W_Voice e -> $"W_Voice {e.eventType}"
            | W_Reasoner (id,_) -> $"W_Reasoner ({id})"
            | W_Err e -> $"W_Error {e}"        
            

type WBus<'appIn,'appOut> = 
    {
        ///use PostInput function instead of directly using this property, for consistent logging
        _inputChannel  : Channel<W_Msg<'appIn>>
        postOutput  : 'appOut -> unit
    }
    with 
        static member Create<'appIn,'appOut> (post:'appOut->unit) = 
            {
                _inputChannel  = Channel.CreateBounded<W_Msg<'appIn>>(10)
                postOutput = post
            }
        member this.Close() = this._inputChannel.Writer.TryComplete() |> ignore
        member this.PostInput msg = 
            match this._inputChannel.Writer.TryWrite msg with 
            | false -> Log.warn $"Bus dropped message {msg}"
            | true  -> ()
        

///A type that represents a state where 'state' is a function that takes an event and returns 
///the next state + a list output events
type F<'Event,'OutEvent> = F of ('Event -> Async<F<'Event,'OutEvent>>)*'OutEvent list

module Workflow =
    let ReasonerMsgWithCorrId (msg:FsResponses.Response) =
        let corrId = 
            msg.metadata
            |> Option.bind (fun m -> m |> Map.tryFind C.CORR_ID)
            |> Option.defaultValue ""
        W_Reasoner(corrId,msg)
   
    ///accepts current state and input event,
    ///returns nextState and publishes any output events
    let private transition bus state event = async {
        let! (F(nextState,outEvents)) = state event
        outEvents |> List.iter bus.postOutput
        return nextState
    }

    let run (token:CancellationToken) bus initState =
        let runner =  
            bus._inputChannel.Reader.ReadAllAsync(token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.map(fun m -> Log.info $"Workflow message: {m.msgType}"; m)
            |> AsyncSeq.scanAsync (transition bus) initState
            |> AsyncSeq.iter (fun x -> ())

        let catcher = 
            async {
                match! Async.Catch runner with 
                | Choice1Of2 _   -> Log.info $"Workflow done"
                | Choice2Of2 exn -> (WE_Exn >> W_Err >> bus.PostInput) exn
                                    Log.exn(exn,"Workflow.run")                
            }

        Async.Start(catcher,token)
