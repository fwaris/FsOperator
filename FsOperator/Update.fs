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
        //let runState = {runState with chatHistory = testChat; chatState = ChatState.CS_Loop}
        let runState = {runState with chatMode = CM_Text testChat; chatState = ChatState.CS_Prompt}
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

    let token() = new System.Threading.CancellationTokenSource()

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | BrowserConnected -> {model with initialized=true},Cmd.none
            | TextChat_StartStopTask ->
                match model.runState with 
                | None when model.initialized -> 
                    let runState = RunState.Create model.mailbox model.instructions
                    let runState = {runState with chatMode = CM_Text []; chatState = CS_Loop}
                    ComputerUse.startMessaging runState
                    ComputerUse.sendStartMessage runState (Instructions.getTextChat model.instructions) |> Async.Start
                    ComputerUse.loop runState 
                    {model with runState =  Some runState}, Cmd.none
                | None -> model, Cmd.none
                | Some runState when runState.chatMode.IsCM_Text -> 
                    runState.tokenSource.Cancel()
                    runState.fromModel.Writer.TryComplete() |> ignore
                    runState.toModel.Writer.TryComplete()   |> ignore                    
                    {model with runState=Some {runState with chatState = CS_Init}}, Cmd.none 
                | _ -> model, Cmd.none //ignore voice mode
            | StopIfRunning -> model, (if model.runState.IsSome then Cmd.ofMsg TextChat_StartStopTask else Cmd.none)

            | SetInstructions txt -> {model with instructions=Instructions.setTextChat txt model.instructions}, Cmd.none

            | Chat_UpdateQuestion txt -> {model with runState = RunState.setQuestion txt model.runState}, Cmd.none
            
            | Chat_Append msg -> {model with runState = RunState.appendChatMsg msg model.runState}, Cmd.none

            | Chat_HandleTurnEnd -> 
                let rs = RunState.setState CS_Prompt model.runState
                let cmd = 
                    match rs with 
                    | Some r when r.chatMode.IsCM_Voice ->                 
                        let callId = RunState.lastFunctionCallId rs
                        let asstMsg = RunState.lastAssistantMessage rs
                        let conn = RunState.voiceConnection rs
                        match conn,asstMsg,callId with
                        | Some cnn, Some m, Some callId -> Functions.sendFunctionResponse cnn m.content callId; Cmd.none
                        | None,_,_ ->  Cmd.ofMsg (TerminateWithMessage "no voice connection")
                        | _,None,_ -> Cmd.ofMsg (TerminateWithMessage  "no assistant message as the last message of chat")
                        | _,_,None -> Cmd.ofMsg (TerminateWithMessage "no function call id found to respond to voice assistant")
                    | _ -> Cmd.none
                {model with runState=rs}, cmd

            | Chat_Submit ->               
                {model with 
                    runState = 
                        model.runState 
                        |> RunState.appendChatMsg model.question
                        |> RunState.setQuestion ""
                        |> RunState.setState CS_Loop

                        |> 
                    log = ("Chat submitted"::model.log) |> List.truncate 100
                }, Cmd.none}
                let rs = 
                    model.runState
                    |> RunState.appendChatMsg model.question

                let rs = 
                    model.runState
                    |> Option.bind (RunState.setState CS_Prompt)


                match model.runState with
                | Some runState when runState.chatState.IsCS_Prompt -> 
                    let prevId = runState.lastCuaResponse.Value |> Option.map (fun r -> r.id)
                    let question = runState.question
                    let runState = RunState.appendChatMsg runState {runState with 
                                        chatState = CS_Loop
                                        chatMode = match chatMode with 
                                                  | CM_Text -> chatHistory = runState.chatHistory |> Chat.append (User runState.question)
                                        question = ""
                                   }
                    ComputerUse.loop runState
                    ComputerUse.sendTextResponse runState (prevId,question) |> Async.Start
                    {model with runState = Some runState}, Cmd.none
                | _ -> model, Cmd.none
            | AppendLog txt -> {model with log = (txt:: model.log) |> List.truncate 100}, Cmd.none
            | ClearLog -> {model with log = []}, Cmd.none
            | SetUrl txt -> {model with url=txt}, Cmd.none

            | SetAction txt -> {model with action=txt}, Cmd.none
            | StatusMsg_Clear dt -> (if shouldClearStatus dt (fst model.statusMsg) then  {model with statusMsg = None,""} else model), Cmd.none
            | StatusMsg_Set txt -> let t = DateTime.Now in {model with statusMsg = Some t,txt}, Cmd.OfAsync.perform  delayClearStatus t StatusMsg_Clear

            | TurnEnd -> model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set "assistant done its turn"); Cmd.ofMsg Chat_HandleTurnEnd]
            | TerminateWithException ex -> if model.runState.IsNone then model,Cmd.none else model, Cmd.batch [Cmd.ofMsg (StatusMsg_Set ex.Message);  Cmd.ofMsg StopIfRunning]
            | TestSomething -> testSomething model

            | VoicChat_StartStop -> 
                match model.runState with 
                | None when model.initialized -> 
                    let runState = RunState.Create model.mailbox model.instructions
                    let conn = RTOpenAI.Api.Connection.create()
                    let runState = let rs = runState |> RunState.appendChatMsg runState.question in {rs with chatState = CS_Loop; chatMode = CM_Voice conn}
                    ComputerUse.startMessaging runState
                    ComputerUse.loop runState 
                    VoiceMachine.startVoiceMachine runState |> Async.Start
                    {model with runState =  Some runState}, Cmd.none
                | None -> model, Cmd.none
                | Some runState when runState.chatMode.IsCM_Voice -> 
                    runState.tokenSource.Cancel()
                    runState.fromModel.Writer.TryComplete() |> ignore
                    runState.toModel.Writer.TryComplete()   |> ignore                    
                    VoiceMachine.stopVoiceMachine runState |> Async.Start
                    {model with runState=Some {runState with chatState = CS_Init}}, Cmd.none 
                | _ -> model, Cmd.none //ignore voice mode

            | VoiceChat_RunInstructions (instructions,ev) ->
                match model.runState with 
                | Some rs when rs.chatMode.IsCM_Voice -> 
                    rs.lastFunctionCallId.Value <- Some ev
                    Functions.sendVoiceInstructions rs instructions |> Async.Start
                | _ -> ()
                model, Cmd.none

            //| _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex 
            model, Cmd.ofMsg (TerminateWithException ex)

