namespace FsOperator
open System.Threading.Channels
open FsOpCore

module TextChat =
    type SubState = {
        t:OpTask
        driver:IUIDriver
        mailbox:Channel<ClientMsg>        
        bus : WBus<ClientMsg,ClientMsg>
    }

    let rec s_start ss msg = async {
        return
            match msg with 
            | W_App Chat_TargetAcquired -> 
                Resps.sendStartCua ss.driver ss.bus ss.t.textModeInstructions                 
                F(s_loop ss,[])
            | _ -> F(s_start ss,[])
    }

    and s_loop ss msg = async {
        match msg with 
        | W_Cua resp -> 
            
            //show any text response from cua model
            Resps.extractText resp
            |> Option.iter (fun txt -> ss.mailbox.Writer.TryWrite(Chat_Append (Assistant {id=resp.id; content=txt})) |> ignore)

                        
            //process computer call
            match Resps.extractComputerCall resp with
            | Some cb ->
                do! Actions.doAction 2 ss.driver cb.action
                return F(s_loop ss,[])

            | None -> return F(s_pause ss,[])
            
        | _               -> return F(s_loop ss,[])         
    }            

    and s_pause ss msg = async {
        return F(s_pause ss,[])
    }


    let create (t:OpTask) =
        let s0 = {|t = t; |}
        let bus = WBus.Create<ClientMsg,ClientMsg>()


        bus,s0,transition
        
 
