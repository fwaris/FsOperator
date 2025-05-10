namespace FsOperator
open System
open System.Threading.Channels
open Elmish
open FSharp.Control
open Avalonia.FuncUI.Hosts

module Update =
    let subscribeBackground (model:Model) =
        let backgroundEvent dispatch =
            let ctx = new System.Threading.CancellationTokenSource()
            let comp =
                async{
                    let comp =
                         model.mailbox.Reader.ReadAllAsync()
                         |> AsyncSeq.ofAsyncEnum
                         |> AsyncSeq.iter dispatch
                    match! Async.Catch(comp) with
                    | Choice1Of2 _ -> printfn "dispose subscribeBackground"
                    | Choice2Of2 ex -> printfn "%s" ex.Message
                }
            Async.Start(comp,ctx.Token)
            {new IDisposable with member _.Dispose() = ctx.Dispose(); printfn "disposing subscription backgroundEvent";}
        backgroundEvent

    let subscriptions model =

        let sub2 = subscribeBackground model
        [
            [nameof sub2], sub2
        ]

    let testSomething (model:Model) =
        async {
            try
                //do! Preview.drawArrow 100 500 50 0. 2000
                do! Preview.drawClick 100 500 2000
                //do! Preview.drawDragArrow (100,500) (300,600) 2000
                debug "Test something"
            with ex ->
                debug $"Error in testSomething: {ex.Message}"
        }
        |> Async.Start
        model,Cmd.none

    let testSomething_ (model:Model) =
        let testChat = 
            [
                Assistant {id = "1"; content = Instructions.getTextChat model.instructions}
                User "The quick brown fox jumped over the lazy dog"
                Assistant {id = "2"; content = "How can I help you?"}
            ]
        let runState = RunState.Create model.mailbox model.instructions
        let runState = {runState with chatMode = CM_Text {Chat.Default with messages=testChat} ; cuaState = CUAState.CUA_Pause}
        {model with runState =  Some runState}, Cmd.none


    let init _   = 
        let url,instructions = StartPrompts.amazon
        //let url,instructions = StartPrompts.netflix
        //let url,instructions = StartPrompts.linkedIn
        //let url,instructions = StartPrompts.twitter
        //sResponses.debug_logging <- true
        let model = {
            instructions = Instructions.sample
            runState = None
            mailbox = Channel.CreateBounded(10)
            log = []
            output = ""
            initialized = false
            url = url
            webview = ref None
            action = ""
            statusMsg = None,""
        }        
        model,Cmd.ofMsg Initialize

    let shouldClearStatus (inComingDT:DateTime option) messageDT = 
        match inComingDT,messageDT with
        | None, None -> true
        | Some inComingDT, None -> true
        | Some inComingDT, Some messageDT -> messageDT = inComingDT
        | None, Some messageDT -> false

    let delayClearStatus (time:DateTime) =
        async {
            do! Async.Sleep 10000
            return Some time
        }    

    ///start or stop text mode task
    let startStopForTextChat model =
        match model.runState with 
        | None when model.initialized -> 
            let runState = {RunState.initForText model.mailbox model.instructions with cuaState = CUA_Loop}
            ComputerUse.startMessaging (runState.tokenSource.Token,runState.bus)
            ComputerUse.sendStartMessage runState.bus (Instructions.getTextChat model.instructions) |> Async.Start
            ComputerUse.loop runState 
            {model with runState =  Some runState}, Cmd.none
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Text -> 
            runState.tokenSource.Cancel()
            runState.bus.fromCua.Writer.TryComplete() |> ignore
            runState.bus.toCua.Writer.TryComplete()   |> ignore                    
            {model with runState=Some {runState with cuaState = CUA_Init}}, Cmd.none 
        | _ -> model, Cmd.none //ignore any other state

    ///start or stop voice mode task
    let startStopForVoiceChat (model:Model) = 
        match model.runState with 
        | None when model.initialized -> 
            let runState = {RunState.initForVoice model.mailbox model.instructions with cuaState = CUA_Loop}
            ComputerUse.startMessaging (runState.tokenSource.Token, runState.bus)
            ComputerUse.loop runState 
            VoiceMachine.startVoiceMachine runState |> Async.Start
            {model with runState =  Some runState}, Cmd.none
        | None -> model, Cmd.none
        | Some runState when runState.chatMode.IsCM_Voice -> 
            runState.tokenSource.Cancel()
            runState.bus.fromCua.Writer.TryComplete() |> ignore
            runState.bus.toCua.Writer.TryComplete()   |> ignore                    
            VoiceMachine.stopVoiceMachine runState |> Async.Start
            {model with runState = Some {runState with cuaState = CUA_Init}}, Cmd.none
        | _ -> model, Cmd.none //ignore voice mode

    ///cua assistant loop has stopped and we need to respond to the assistant's last message (and chatHistory)
    ///For voice mode we send the cua assistant's last message to the voice assistant
    ///For text, no action required (for now until reasoning is enabled) the user can see the message and respond 
    let handleTurnEnd model =
        match model.runState with 
        | Some r when r.chatMode.IsCM_Voice -> 
            let rs = RunState.setState CUA_Pause model.runState
            let callId = RunState.lastFunctionCallId rs
            let asstMsg = RunState.lastAssistantMessage rs
            let conn = RunState.voiceConnection rs
            match conn,asstMsg,callId with
            | Some cnn, Some m, Some callId -> VoiceAsst.sendFunctionResponse cnn m.content callId
            | None,_,_ ->  failwith "no voice connection"
            | _,None,_ -> failwith  "no assistant message as the last message of chat"
            | _,_,None -> failwith "no function call id found to respond to voice assistant"
            {model with runState=rs}, Cmd.none
        | _ -> model,Cmd.none                 
        
    ///Either the voice assistant, the reasoning model or the user has submitted a question (i.e. a response)
    ///Send that to the CUA assitant to continue the task
    let resumeChat (model:Model) = 
        let question = RunState.question model.runState
        let prevCuaId = model.runState |> RunState.lastCuaResponse |> Option.bind(_.previous_response_id)
        let model =
            {model with 
                runState = 
                    model.runState 
                    |> RunState.appendChatMsg (User question)
                    |> RunState.setQuestion ""
                    |> RunState.setState CUA_Loop}
        match model.runState with
        | Some rs when rs.chatMode.IsCM_Text -> ComputerUse.sendTextResponse rs.bus (prevCuaId,question) |> Async.Start
        | Some rs when rs.chatMode.IsCM_Voice -> VoiceAsst.sendVoiceInstructions rs.bus (prevCuaId,question) |> Async.Start
        | _ -> ()
        model,Cmd.none              
   
    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | BrowserConnected -> {model with initialized=true},Cmd.none
            | TextChat_StartStopTask -> startStopForTextChat model
            | SetInstructions txt -> {model with instructions=Instructions.setTextChat txt model.instructions}, Cmd.none
            | Chat_UpdateQuestion txt -> {model with runState = RunState.setQuestion txt model.runState}, Cmd.none            
            | Chat_Append msg -> {model with runState = RunState.appendChatMsg msg model.runState}, Cmd.none
            | Chat_HandleTurnEnd -> handleTurnEnd model
            | Chat_Submit ->  resumeChat model             
            | AppendLog txt -> {model with log = (txt:: model.log) |> List.truncate 100}, Cmd.none
            | ClearLog -> {model with log = []}, Cmd.none
            | SetUrl txt -> {model with url=txt}, Cmd.none

            | SetAction txt -> {model with action=txt}, Cmd.none
            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear

            | TurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | Abort (ex,msg) -> if model.runState.IsNone then model,Cmd.none else model, Cmd.ofMsg (StatusMsg_Set (ex |> Option.map _.Message |> Option.defaultValue msg))
            | TestSomething -> testSomething model

            | VoicChat_StartStop -> startStopForVoiceChat model

            | VoiceChat_RunInstructions (instructions,ev) ->
                match model.runState with 
                | Some rs when rs.chatMode.IsCM_Voice -> 
                    rs.lastFunctionCallId.Value <- Some ev
                    VoiceAsst.sendVoiceInstructions rs.bus (None,instructions) |> Async.Start
                | _ -> ()
                model, Cmd.none

            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex 
            model, Cmd.ofMsg (Abort (Some ex,"elmish loop"))

