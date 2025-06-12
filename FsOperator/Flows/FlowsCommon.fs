namespace FsOperator
open FsOpCore
open System.Threading

module FlUtils = 
    ///utility operator to create default workflow states
    let (!!) s = F(s,[])

    let snapshot (driver:IUIDriver) = async {
        let! (snapshot,(w,h)) = driver.snapshot()
        let! url = driver.url()
        return (snapshot,w,h,url,driver.environment)
    }

//utility functions for working Responses API messsages
module FlResps =
    open FsResponses

    let temperature = 0.f

    ///general exception handler for async computations - traps and posts error as W_Err message to input channel
    let catch replyChannel (comp:Async<'t>) =   
        async{
            match! Async.Catch(comp) with 
            | Choice2Of2 exn -> Log.exn(exn,"Resps.catch")
                                replyChannel (W_Err (WE_Exn exn)) 
            | _              -> ()
        }
        |> Async.Start

    ///extract any text message in resonse
    let extractText (response:FsResponses.Response) = 
        RUtils.outputText response
        |> checkEmpty

    let computerCall (response:FsResponses.Response) = 
        response.output
        |> List.choose (function 
            | Computer_call cb -> Some cb
            | _                -> None)
        |> List.tryHead

    ///attempt to extract the 'computer call id' from a response
    let lastCallId (resp:FsResponses.Response) =
        resp.output
        |> List.choose (function
            | Computer_call cb -> Some cb.call_id
            | _ -> None)
        |> List.rev
        |> List.tryHead        

    let safetyChecks (resp:FsResponses.Response) = 
        resp.output 
        |> List.choose (function Computer_call cb -> Some cb.pending_safety_checks | _ -> None) 
        |> List.concat


    ///send a request to the responses api (with retry) and post response back to input channel
    let rec private sendWithRetry<'t> count msgWrap (replyChannel:W_Msg<'t>->unit) (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                replyChannel (msgWrap response) 
            with ex ->
                if count < 2 then
                    Log.warn $"responses api send error: retry {count + 1}"
                    return! sendWithRetry (count + 1) msgWrap replyChannel req
                else
                    Log.error $"responses api unable to reconnect aborting"
                    return raise ex
        }

    ///post request to respones api
    let sendRequest msgWrap replyChannel msg = 
        sendWithRetry 2 msgWrap replyChannel msg

    ///send an initial 'computer tool call' request
    let sendStartCua replyChannel instructions (sanpshot,width,height,url,environment) =
       async {
            let contImg = Input_image {|image_url = sanpshot|}
            let input = { Message.Default with content=[contImg]}
            let tool = Tool_Computer_use {|display_height = height; display_width = width; environment = environment|}
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
            do! sendRequest W_Cua replyChannel req 
        }
        |> catch replyChannel

    ///send the 'computer call' result back to the CUA model
    let continueCua replyChannel (prevResp:FsResponses.Response) (sanpshot,width,height,url,environment) =
       async {
            let tool = Tool_Computer_use {|display_height = height; display_width = width; environment = environment|}            
            let lastCallId = lastCallId prevResp |> Option.defaultWith(fun _ -> failwith $"call_id not found in previous response")
            let safetyChecks = safetyChecks prevResp 
            let cc_out = {
                call_id = lastCallId
                acknowledged_safety_checks = safetyChecks  //these should come from human acknowlegedgement
                output = Computer_creenshot {|image_url = sanpshot |}
                current_url = url
            }
            let req = {Request.Default with
                            input = [Computer_call_output cc_out]; tools=[tool]
                            previous_response_id = Some prevResp.id
                            store = true
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            do! sendRequest W_Cua replyChannel req 
        }
        |> catch replyChannel

