namespace FsOperator
open FsOpCore
open System.Threading

//utility functions for working Responses API messsages
module Resps =
    open FsResponses

    let temperature = 0.f

    let extractComputerCall (response:FsResponses.Response) = 
        response.output
        |> List.tryPick (function 
            | Computer_call cb -> Some cb
            | _                -> None
        )

    let extractText (response:FsResponses.Response) = 
        RUtils.outputText response
        |> checkEmpty

    let rec sendWithRetry<'t> count msgWrap (replyChannel:Channels.Channel<W_Msg<'t>>) (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                replyChannel.Writer.TryWrite (msgWrap response) |> ignore
            with ex ->
                if count < 2 then
                    Log.warn $"responses api send error: retry {count + 1}"
                    return! sendWithRetry (count + 1) msgWrap replyChannel req
                else
                    Log.error $"responses api unable to reconnect aborting"
                    return raise ex
        }

    let sendRequest msgWrap replyChannel msg = 
        sendWithRetry 2 msgWrap replyChannel msg |> Async.Start

    let sendStartCua (driver:IUIDriver) bus instructions =
       async {
            let! imgUrl,(w,h) = driver.snapshot()
            let contImg = Input_image {|image_url = imgUrl|}
            let input = { Message.Default with content=[contImg]}
            let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
            let req = {Request.Default with
                            input = [Message input]; tools=[tool]
                            instructions = instructions
                            previous_response_id = None
                            store = true
                            temperature = temperature
                            reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            sendRequest W_Cua bus.inCh req 
        }
        |> Async.Start

module FUtils = 
    ///utility operator to create default workflow states
    let (!!) s = F(s,[])
