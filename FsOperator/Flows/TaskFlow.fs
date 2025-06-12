namespace FsOperator
open System.Threading
open System.Threading.Channels
open FsOpCore
open FlUtils

module TaskFlow =
    let MAX_SNAPSHOTS = 3
    
    ///flow input messages
    type TaskFLowMsgIn =
        | TFi_Start 
        | TFi_Resume of Chat
        | TFi_ChatUpdated of Chat

    ///messages output by flow
    type TaskFLowMsgOut =
        | TFo_Paused of Chat
        | TFo_ChatUpdated of Chat
        | TFo_Error of WErrorType
        | TFo_Action of string

    type SubState = {
        cts         : CancellationTokenSource
        chat        : Chat
        driver      : IUIDriver       
        bus         : WBus<TaskFLowMsgIn,TaskFLowMsgOut>
        snapshots   : string list
    }
        with 
            member this.appendSnapshot s = {this with snapshots = s::this.snapshots |> List.truncate MAX_SNAPSHOTS }
            member this.appendMsg msg = {this with chat = Chat.append msg this.chat}


    ///handle Cua respones to potentially perform a computer call
    let performCall (ss:SubState) resp = async {

        //extract and post any text response from cua model
        let ss,outMsgs = 
            FlResps.extractText resp
            |> Option.map (fun txt ->  
                let ss = ss.appendMsg (Assistant {id=resp.id; content=txt})
                ss,[TaskFLowMsgOut.TFo_ChatUpdated ss.chat])
            |> Option.defaultValue (ss,[])

        //take snapshot
        let! (snapshot,w,h,url,env) = snapshot ss.driver
        let ss = ss.appendSnapshot snapshot      

        //process computer call
        let! (ss,outMsgs) = 
            match FlResps.computerCall resp with
            | Some cb -> 
                async {
                    do! Actions.doAction 2 ss.driver cb.action
                    FlResps.continueCua ss.bus.PostIn resp (snapshot,w,h,url,env)
                    return ss,(TFo_Action (Actions.actionToString cb.action))::outMsgs
                }
            | None -> async{ return ss,outMsgs }

        return ss,outMsgs
    }

    (* --- states --- *)

    let rec s_start ss msg = async {    
        match msg with 
        | W_Err e         -> return !!(s_terminate ss e)
        | W_App TFi_Start -> let! (snapshot,w,h,url,env) = snapshot ss.driver
                             let ss = ss.appendSnapshot snapshot
                             FlResps.sendStartCua ss.bus.PostIn ss.chat.systemMessage (snapshot,w,h,url,env)
                             return !!(s_loop ss)
        | _               -> return !!(s_start ss)
    }

    and s_loop ss msg = async {
        match msg with 
        | W_Err e    -> return !!(s_terminate ss e)
        | W_Cua resp -> let! ss,outMsgs = performCall ss resp
                        return F(s_loop ss,outMsgs)
        | _          -> return !!(s_loop ss)
    }            

    and s_pause ss msg = async {   
        match msg with 
        | W_Err e               -> return !!(s_terminate ss e)
        | W_App (TFi_Resume ch) -> return !!(s_loop {ss with chat = ch})
        | _                     -> return !!(s_pause ss)
    }

    and s_terminate ss e msg = async {
        Log.error (string e)
        ss.cts.Cancel()
        ss.bus.post (TFo_Error e)
        return !!(s_terminate ss e)
    }

    (* --- states [end] --- *)

    ///construct flow and also start it
    let create post driver chat : IFlow<TaskFLowMsgIn> =
        let bus = WBus.Create<TaskFLowMsgIn,TaskFLowMsgOut> post

        ///initial substate
        let ss0 = {
            cts=new CancellationTokenSource()
            chat = chat
            bus=bus
            driver=driver; snapshots=[]
        }

        //initial state
        let s0 = s_start ss0

        //start flow
        Workflow.run ss0.cts.Token bus s0

        //return handler to talk to flow
        {new IFlow<TaskFLowMsgIn> with

            member _.Terminate () = 
                ss0.cts.Cancel()
                ss0.bus.inCh.Writer.TryComplete() |> ignore
 
            member _.Post msg = bus.PostIn (W_App msg)
        }

