namespace FsOperator
open System
open FSharp.Control
open PuppeteerSharp
open FsResponses
open System.IO
open PuppeteerSharp.Input

module ComputerUse =    

    let postLog (runState:RunState) msg =  runState.mailbox.Writer.TryWrite(ClientMsg.AppendLog msg) |> ignore
    let postAction (runState:RunState) action = runState.mailbox.Writer.TryWrite(SetAction action) |> ignore
    let postWarning (runState:RunState) warning = runState.mailbox.Writer.TryWrite(StatusMsg_Set warning) |> ignore

    let rec sendWithRetry count (runState:RunState) (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                return response
            with ex ->
                if count < 2 then 
                    postLog runState $"send error: retry {count + 1}"
                    return! sendWithRetry (count + 1) runState req
                else
                    postLog runState $"Unable to reconnect aborting"
                    return raise ex
        }
  
    let startMessaging (runState:RunState) =
        let sendLoop = 
            runState.toModel.Reader.ReadAllAsync(runState.tokenSource.Token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun request ->
                async {
                    postLog runState $"--> {request}"
                    let! response = sendWithRetry 0 runState request                    
                    postLog runState $"<-- {response}"    
                    do! runState.fromModel.Writer.WriteAsync(response,runState.tokenSource.Token).AsTask() |> Async.AwaitTask
                }
            )
        let comp = 
            async {
                match! Async.Catch sendLoop with 
                | Choice1Of2 _ -> debug "dispose sendLoop"
                | Choice2Of2 ex -> 
                    debug $"Error in sendLoop: %s{ex.Message}"                    
                    runState.mailbox.Writer.TryWrite(StopWithError ex) |> ignore
            }
        Async.Start(comp, runState.tokenSource.Token)

    let sendStartMessage (runState:RunState) =
       async {
                let! imgUrl,(w,h) = Browser.snapshot()
                let contImg = Input_image {|image_url = imgUrl|}
                let input = { Message.Default with content=[contImg]}
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                let req = {Request.Default with 
                                input = [Message input]; tools=[tool]
                                instructions = Some runState.instructions
                                previous_response_id = None                    
                                store = true
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }
                do! runState.toModel.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
        }


    let sendTextResponse (runState:RunState) (previousId:string option,message:string) =
       async {
                try
                    let! imgUrl,(w,h) = Browser.snapshot()                    
                    let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                    let contMsg = Message {id = None; role="user"; content = [Input_text {|text = message|}] ; status = None}
                    let req = {Request.Default with 
                                    input = [contMsg]; tools=[tool]
                                    previous_response_id = previousId
                                    store = true
                                    model=Models.computer_use_preview
                                    truncation = Some Truncation.auto
                              }
                    do! runState.toModel.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
                with ex ->
                    debug $"Error in sendTextResponse: %s{ex.Message}"
        }


    let getResponseIdsAndChecks (runState:RunState) = 
        runState.lastResponse.Value
        |> Option.bind (fun r -> 
            let safetyChecks = r.output |> List.choose (function Computer_call cb -> Some cb.pending_safety_checks | _ -> None) |> List.concat
            r.output
            |> List.choose (function
                | Computer_call cb -> Some cb.call_id
                | _ -> None)
            |> List.rev
            |> List.tryHead
            |> Option.map (fun cbId -> r.id, cbId, safetyChecks) //return the response id and the computer call id)
        )

    let computerCallResponse (runState:RunState) =
        async {            
            let! page = Browser.page()
            match getResponseIdsAndChecks runState with
            | None -> 
                runState.mailbox.Writer.TryWrite(AppendLog "turn end") |> ignore
                runState.mailbox.Writer.TryWrite(TurnEnd) |> ignore
                return ()
            | Some (prevId, lastCallId, safetyChecks) -> 
                let! imgUrl,(w,h) = Browser.snapshot()
                let contImg = Input_image {|image_url = imgUrl|} //use the same image url as before
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                debug $"snapshot dims : {w}, {h}"
                let cc_out = {
                    call_id = lastCallId
                    acknowledged_safety_checks = safetyChecks                                 //these should come from human acknowlegedgement
                    output = Computer_creenshot {|image_url = imgUrl |}
                    current_url = Some page.Url
                }
                let req = {Request.Default with 
                                input = [Computer_call_output cc_out]; tools=[tool]
                                previous_response_id = Some prevId
                                store = true
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }

                do! runState.toModel.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
        }

                        
    let loop (runState:RunState) = 
        let rec loop() = 
            async {  
                try 
                    let! response = runState.fromModel.Reader.ReadAsync(runState.tokenSource.Token).AsTask() |> Async.AwaitTask 
                    if runState.tokenSource.IsCancellationRequested |> not then 
                        runState.lastResponse.Value <- Some response
                        let mutable hasComputerCall = false
                        for o in response.output do
                            match o with
                            | Computer_call cb -> 
                                hasComputerCall <- true
                                cb.pending_safety_checks |> List.map _.message |> String.concat "," |> shorten 200 |> postWarning runState
                                cb.action |> Actions.actionToString |> postAction runState
                                do! Async.Sleep 500
                                //do! Preview.previewAction 2000 cb.action
                                do! Actions.doAction 2 cb.action 
                                do! Async.Sleep 1000
                                do! computerCallResponse runState
                            | Message m -> 
                                let outputText = RUtils.outputText response
                                let msg = Assistant {id = response.id; content = outputText}
                                runState.mailbox.Writer.TryWrite(ClientMsg.Chat_Append msg) |> ignore
                            | _  -> ()
                        if runState.tokenSource.IsCancellationRequested |> not then 
                            if hasComputerCall then 
                                return! loop()
                            else
                                runState.mailbox.Writer.TryWrite(ClientMsg.TurnEnd) |> ignore
                with ex -> 
                    debug $"Error in loop: %s{ex.Message}"
                    runState.mailbox.Writer.TryWrite(ClientMsg.StopWithError ex) |> ignore
                    do! Async.Sleep 1000
            }
        Async.Start(loop(),runState.tokenSource.Token)
        
        

    