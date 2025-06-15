namespace FsOperator
open FSharp.Control
open FsResponses
open FsOperator
open FsOpCore

module ComputerUse =
    let temperature = 0.0f

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

    let sendStartMessage (driver:IUIDriver) bus instructions  =
       async {
                let! imgUrl,(w,h) = driver.snapshot()
                let contImg = Input_image {|image_url = imgUrl|}
                let input = { Message.Default with content=[contImg]}
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                let req = {Request.Default with
                                input = [IOitem.Message input]; tools=[tool]
                                instructions = Some instructions
                                previous_response_id = None
                                store = true
                                temperature = temperature
                                reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }
                req |> Bus.postToCua bus
        }

    let sendTextResponse (driver:IUIDriver) bus (instructions: string option, messages:Message list) =
       async {
            try
                let! imgUrl,(w,h) = driver.snapshot()
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                //let contMsg = Message {id = None; role="user"; content = [Input_text {|text = message|}] ; status = None}
                let messages = messages |>  List.map IOitem.Message
                let req = {Request.Default with
                                input = messages; tools=[tool]
                                previous_response_id = None
                                instructions = instructions
                                store = true
                                temperature = temperature
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
            let safetyChecks = r.output |> List.choose (function IOitem.Computer_call cb -> Some cb.pending_safety_checks | _ -> None) |> List.concat
            r.output
            |> List.choose (function
                | IOitem.Computer_call cb -> Some cb.call_id
                | _ -> None)
            |> List.rev
            |> List.tryHead
            |> Option.map (fun cbId -> r.id, cbId, safetyChecks) //return the response id and the computer call id)
        )

    let summarizationPrompt taskInstructions = """The user has tasked an automated 'computer assistant'
to accomplish a task as given in the TASK INSTRUCTIONS below. The computer
assistant has operated the computer in pursuit of the task. Along the way it has
taken some screenshots. Give any available message history and the screenshots, summarize the content
obtained thus far, in relation to the task instructions.

# TASK INSTRUCTIONS
{taskInstructions}
"""

    ///if the cua model is not able to produce a summary, use the reasoner model to do the same, as a fallback
    let summarizeProgressReasoner (instructions: string option, messages:Message list, screenshots:string list) =
        async {
            try
                let txt = Content.Input_text {|text = "Summarize and report"|}
                let imgs = screenshots |> List.map(fun i -> Content.Input_image {|image_url=i|})
                let msg = {Message.Default with content = [txt] @ imgs}//contImgs}
                let chatHistory = messages |> List.map IOitem.Message
                let msgInput = IOitem.Message msg
                let req = {Request.Default with
                                    input = chatHistory @ [msgInput];
                                    instructions = instructions |> Option.map summarizationPrompt
                                    store = false
                                    model=Models.gpt_41
                                    truncation = Some Truncation.auto
                                }
                let! resp = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                let txt = RUtils.outputText resp
                return (resp.id,txt)
            with ex ->
                Log.exn(ex,"summarizeProgressReasoner")
                return raise ex
        }

    ///early stop the CUA model interaction and ask the model summarize
    ///the results thus far
    let summarizeProgressCua (driver:IUIDriver,taskState:TaskState) =
        async {
            try
                match getResponseIdsAndChecks taskState with
                | None ->
                    return failwith "Not enough context to summarize"
                | Some (prevId, lastCallId, safetyChecks) ->
                    let! imgUrl,(w,h) = driver.snapshot()
                    TaskState.appendScreenshot imgUrl (Some taskState)
                    let! imgUrl,(w,h) = driver.snapshot()
                    let images = TaskState.screenshots (Some taskState)
                    let txt = Content.Input_text {|text = "Summarize and report the current results"|}
                    let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                    let msg = {Message.Default with content = txt::[]}//contImgs}
                    let msgInput = IOitem.Message msg
                    let! url = driver.url()
                    let cc_out = {
                        call_id = lastCallId
                        acknowledged_safety_checks = safetyChecks                                 //these should come from human acknowlegedgement
                        output = Computer_screenshot {|image_url = imgUrl |}
                        current_url = url
                    }
                    let req = {Request.Default with
                                        input = [IOitem.Computer_call_output cc_out; msgInput]; tools=[tool]
                                        store = false
                                        previous_response_id = Some prevId
                                        model=Models.computer_use_preview
                                        truncation = Some Truncation.auto
                                  }
                    let! resp = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                    let txt = RUtils.outputText resp
                    return (resp.id,txt)
                with ex ->
                    Log.exn(ex,"summarizeProgressCua")
                    return raise ex
        }

    let computerCallResponse (driver:IUIDriver) (taskState:TaskState) =
        async {
            match getResponseIdsAndChecks taskState with
            | None ->
                taskState.bus.mailbox.Writer.TryWrite(Log_Append "turn end") |> ignore
                taskState.bus.mailbox.Writer.TryWrite(Chat_CUATurnEnd) |> ignore
                return ()
            | Some (prevId, lastCallId, safetyChecks) ->
                let! imgUrl,(w,h) = driver.snapshot()
                let! url = driver.url()
                TaskState.appendScreenshot imgUrl (Some taskState)
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                debug $"snapshot dims : {w}, {h}"
                let cc_out = {
                    call_id = lastCallId
                    acknowledged_safety_checks = safetyChecks                                 //these should come from human acknowlegedgement
                    output = Computer_screenshot {|image_url = imgUrl |}
                    current_url = url
                }
                let req = {Request.Default with
                                input = [IOitem.Computer_call_output cc_out]; tools=[tool]
                                previous_response_id = Some prevId
                                store = true
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }

                do! taskState.bus.toCua.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask
        }

    let startCuaLoop driver (taskState:TaskState) =
        let rec loop retryCount =
            async {
                try
                    let! response = taskState.bus.fromCua.Reader.ReadAsync(taskState.tokenSource.Token).AsTask() |> Async.AwaitTask
                    if taskState.tokenSource.IsCancellationRequested |> not then
                        taskState.lastCuaResponse.Value <- Some response
                        let mutable hasComputerCall = false
                        for o in response.output do
                            match o with
                            | IOitem.Computer_call cb ->
                                hasComputerCall <- true
                                cb.pending_safety_checks |> List.map _.message |> String.concat "," |> shorten 200 |> Bus.postWarning taskState.bus
                                cb.action |> Actions.actionToString |> Bus.postAction taskState.bus
                                //do! Async.Sleep 100
                                do! Actions.doAction 2 driver cb.action
                                //do! Async.Sleep 1000
                                do! computerCallResponse driver taskState
                            | IOitem.Message m ->
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
                    do! Async.Sleep 1000
                    if retryCount < 10 then
                        return! loop(retryCount + 1)
                    else
                        Log.exn (ex,"Error in loop")
                        Abort (Some ex,$"Error in loop: %s{ex.Message}") |> Bus.postMessage taskState.bus
            }
        Async.Start(loop 0,taskState.tokenSource.Token)

