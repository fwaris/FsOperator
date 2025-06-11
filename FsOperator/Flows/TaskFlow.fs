namespace FsOperator
open System.Threading
open System.Threading.Channels
open FsOpCore
open FUtils

module TaskFlow =
    
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
        cts     : CancellationTokenSource
        chat    : Chat
        driver  : IUIDriver       
        bus     : WBus<TaskFLowMsgIn,TaskFLowMsgOut>
    }

    let rec s_start ss msg = async {    
        match msg with 
        | W_Err e         -> return !!(s_terminate ss e)
        | W_App TFi_Start -> Resps.sendStartCua ss.driver ss.bus ss.chat.systemMessage
                             return !!(s_loop ss)
        | _               -> return !!(s_start ss)
    }

    and s_loop ss msg = async {
        match msg with 
        | W_Err e -> return !!(s_terminate ss e)
        | W_Cua resp ->             
            //show any text response from cua model
            let ss = 
                Resps.extractText resp
                |> Option.map (fun txt ->  {ss with chat = Chat.append (Assistant {id=resp.id; content=txt}) ss.chat})
                |> Option.defaultValue ss
                        
            //process computer call
            match Resps.extractComputerCall resp with
            | Some cb -> do! Actions.doAction 2 ss.driver cb.action
                         return F(s_loop ss,[TFo_Action (Actions.actionToString cb.action)])
            | None    -> return !!(s_pause ss)            

        | _  -> return !!(s_loop ss)
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

    let create post driver chat : IFlow<TaskFLowMsgIn> =
        let bus = WBus.Create<TaskFLowMsgIn,TaskFLowMsgOut> post
        let ss0 = {cts=new CancellationTokenSource(); chat = chat; bus=bus; driver=driver}
        let s0 = s_start ss0
        Workflow.run ss0.cts.Token bus s0
        {new IFlow<TaskFLowMsgIn> with
            member _.Terminate () = ss0.cts.Cancel()          
            member _.Post msg = bus.PostIn (W_App msg)
        }



        

