namespace FsOperator
open System
open FSharp.Control
open PuppeteerSharp
open FsResponses
open System.IO
open PuppeteerSharp.Input
open FsOperator
open FsOpCore

module ComputerUse =    

    let rec sendWithRetry count bus (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                return response
            with ex ->
                if count < 2 then 
                    Bus.postLog bus $"send error: retry {count + 1}"
                    return! sendWithRetry (count + 1) bus req
                else
                    Bus.postLog bus $"Unable to reconnect aborting"
                    return raise ex
        }
  
    let startApiMessaging (token,bus) =
        let sendLoop =             
            bus.toCua.Reader.ReadAllAsync(token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun request ->
                async {
                    Bus.postLog bus $"--> {RUtils.trimRequest request}"
                    let! response = sendWithRetry 0 bus request                    
                    Bus.postLog bus $"<-- {RUtils.trimResponse response}"                     
                    do! bus.fromCua.Writer.WriteAsync(response,token).AsTask() |> Async.AwaitTask
                }
            )
        let comp = 
            async {
                match! Async.Catch sendLoop with 
                | Choice1Of2 _ -> debug "dispose sendLoop"
                | Choice2Of2 ex -> 
                    Log.exn (ex,"Error in sendLoop")
                    Abort (Some ex,$"Error in sendLoop: %s{ex.Message}") |> Bus.postMessage bus
            }
        Async.Start(comp,token)

    let sendStartMessage bus instructions  =
       async {
                let! imgUrl,(w,h) = Browser.snapshot()
                let contImg = Input_image {|image_url = imgUrl|}
                let input = { Message.Default with content=[contImg]}
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                let req = {Request.Default with 
                                input = [Message input]; tools=[tool]
                                instructions = Some instructions
                                previous_response_id = None                    
                                store = true
                                reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }
                req |> Bus.postToCua bus
        }

    let toMessages (chatMsgs:ChatMsg list) = 
        chatMsgs
        |> List.map (function 
            | ChatMsg.User m -> {id = None; role="user"; content = [Input_text {| text = m |}]; status = None}
            | ChatMsg.Assistant m -> {id = None; role="assistant"; content = [Output_text {text = m.content; annotations=None}] ; status = None})                

    let toChatHistory = function 
        | CM_Text c -> c.systemMessage, toMessages c.messages
        | CM_Voice c -> c.chat.systemMessage, toMessages c.chat.messages
        | CM_Init -> failwith "toChatHistory: CM_Init not supported"

    let truncateHistory messages =
        List.rev messages
        |> List.truncate C.MAX_MESSAGE_HISTORY
        |> List.rev

    let sendTextResponse bus (instructions: string option, messages:Message list) =
       async {
            try
                let! imgUrl,(w,h) = Browser.snapshot()                    
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                //let contMsg = Message {id = None; role="user"; content = [Input_text {|text = message|}] ; status = None}
                let messages = messages |>  List.map InputOutputItem.Message
                let req = {Request.Default with 
                                input = messages; tools=[tool]
                                previous_response_id = None
                                instructions = instructions
                                store = true
                                reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                            }
                req |> Bus.postToCua bus
            with ex ->
                Log.exn (ex,"Error in sendTextResponse")
        }

    let getResponseIdsAndChecks (taskState:TaskState) = 
        taskState.lastCuaResponse.Value
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

    let computerCallResponse (taskState:TaskState) =
        async {            
            let! page = Browser.page()
            match getResponseIdsAndChecks taskState with
            | None -> 
                taskState.bus.mailbox.Writer.TryWrite(Log_Append "turn end") |> ignore
                taskState.bus.mailbox.Writer.TryWrite(Chat_CUATurnEnd) |> ignore
                return ()
            | Some (prevId, lastCallId, safetyChecks) -> 
                let! imgUrl,(w,h) = Browser.snapshot()
                TaskState.appendScreenshot imgUrl (Some taskState)
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

                do! taskState.bus.toCua.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
        }
                        
    let startCuaLoop (taskState:TaskState) = 
        let rec loop retryCount = 
            async {  
                try 
                    let! response = taskState.bus.fromCua.Reader.ReadAsync(taskState.tokenSource.Token).AsTask() |> Async.AwaitTask 
                    if taskState.tokenSource.IsCancellationRequested |> not then 
                        taskState.lastCuaResponse.Value <- Some response
                        let mutable hasComputerCall = false
                        for o in response.output do
                            match o with
                            | Computer_call cb -> 
                                hasComputerCall <- true
                                cb.pending_safety_checks |> List.map _.message |> String.concat "," |> shorten 200 |> Bus.postWarning taskState.bus
                                cb.action |> Actions.actionToString |> Bus.postAction taskState.bus
                                //do! Async.Sleep 100
                                do! Actions.doAction 2 cb.action 
                                //do! Async.Sleep 1000
                                do! computerCallResponse taskState
                            | Message m -> 
                                let outputText = RUtils.outputText response
                                let msg = Assistant {id = response.id; content = outputText}
                                Chat_Append msg |> Bus.postMessage taskState.bus 
                            | _  -> ()
                        if taskState.tokenSource.IsCancellationRequested |> not then 
                            if hasComputerCall then 
                                return! loop 0
                            else
                                Chat_CUATurnEnd |> Bus.postMessage taskState.bus
                with ex -> 
                    debug $"Error in loop: %s{ex.Message}"
                    do! Browser.closeConnection()
                    do! Async.Sleep 1000
                    if retryCount < 10 then 
                        return! loop(retryCount + 1)                   
                    else
                        Log.exn (ex,"Error in loop")
                        Abort (Some ex,$"Error in loop: %s{ex.Message}") |> Bus.postMessage taskState.bus 
            }
        Async.Start(loop 0,taskState.tokenSource.Token)
        
    