namespace FsOpCore
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open System.Text.Json

module FlUtils = 
    ///utility operator to create default workflow states
    let (!!) s = F(s,[])

    let snapshot (driver:IUIDriver) = async {
        let! (snapshot,(w,h)) = driver.snapshot()
        let! url = driver.url()
        return (snapshot,w,h,url,driver.environment)
    }

    /// <summary>
    /// Convert metadata to 'function' tool for use with <see cref="FsResponses.Request" />.
    /// Also see <see cref="FlUtils.functionMetadata" />.
    /// </summary>
    let toFunction (metadata:KernelFunctionMetadata) =     
        {Function.Default with 
            name = metadata.Name
            description = metadata.Description
            parameters = 
                {Parameters.Default with 
                    properties = 
                        metadata.Parameters
                        |> Seq.map (fun (mp:KernelParameterMetadata)  -> 
                            mp.Name,
                            {
                                Property.``type`` = mp.ParameterType.Name.ToLower()
                                Property.description = mp.Description |> checkEmpty |> Option.defaultValue ""
                            }
                        )
                        |> Map.ofSeq           
                    required = 
                        metadata.Parameters 
                        |> Seq.choose (fun p -> if p.IsRequired then Some p.Name else None)
                        |> Seq.toList
                }        
        }
        |> Tool_Function

    ///<summary>
    ///Extract function metadata from a properly annotated type.<br />
    ///Members tagged with KernelFunction("...") attributes are included.<br />
    ///Use <see cref="FlUtils.toFunction"/> to convert metadata to 'function' tool.
    ///</summary>
    let functionMetadata<'t> () =
        let b = Kernel.CreateBuilder()
        b.Plugins.AddFromType<'t>() |> ignore
        let k = b.Build()
        let fs = k.Plugins.GetFunctionsMetadata()
        fs

    ///<summary>
    ///Make a list of 'function' tools that can be used with a <see cref="FsResponses.Request" /><br />
    ///The functions are extracted from a properly annotated type.<br />
    ///See <see cref="FlUtils.functionMetadata"/>.
    ///</summary>
    let makeFunctionTools<'t>() = functionMetadata<'t>() |> Seq.map toFunction |> Seq.toList


    ///call an indivudal function
    let invokeFunction (kernel:Kernel) (name:string) (arguments:string) = async {
        let args = JsonSerializer.Deserialize<Map<string,obj>>(arguments)
        let args = args |> Map.toSeq |> Prompts.kernelArgs          
        let! rslt = kernel.InvokeAsync(pluginName=null,functionName=name,arguments=args) |> Async.AwaitTask
        let str = rslt.GetValue()
        let rsltStr = JsonSerializer.Serialize(str)
        Log.info $"Call {name},{args} --> {rsltStr}"
        return rsltStr
    }        

//utility functions for working Responses API messsages
module FlResps =
    open FsResponses

    let temperature = 0.f

    let toMessages (chatMsgs:ChatMsg list) =
        chatMsgs
        |> List.map (function
            | ChatMsg.User m -> {id = None; role="user"; content = [Input_text {| text = m |}]; status = None}
            | ChatMsg.Assistant m -> {id = None; role="assistant"; content = [Output_text {text = m.content; annotations=None}] ; status = None})    

    let truncateHistory messages =
        List.rev messages
        |> List.truncate C.MAX_MESSAGE_HISTORY
        |> List.rev

    let truncatedChatHistory = toMessages >> truncateHistory

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
            | IOitem.Computer_call cb -> Some cb
            | _                -> None)
        |> List.tryHead

    ///attempt to extract the 'computer call id' from a response
    let lastCallId (resp:FsResponses.Response) =
        resp.output
        |> List.choose (function
            | IOitem.Computer_call cb -> Some cb.call_id
            | _ -> None)
        |> List.rev
        |> List.tryHead        

    let safetyChecks (resp:FsResponses.Response) = 
        resp.output 
        |> List.choose (function IOitem.Computer_call cb -> Some cb.pending_safety_checks | _ -> None) 
        |> List.concat

    let private logApiException (ex:System.Exception) =
        let errMsg = "api send error, retrying ..."
        if ex.InnerException <> null then
            Log.exn(ex.InnerException, errMsg )
        else
            Log.exn(ex, errMsg)

    ///send a request to the responses api (with retry) and post response back to input channel
    let rec private sendWithRetry<'t> count msgWrap (replyChannel:W_Msg<'t>->unit) (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                replyChannel (msgWrap response) 
            with ex ->
                if count < 2 then
                    logApiException ex
                    do! Async.Sleep 2000
                    return! sendWithRetry (count + 1) msgWrap replyChannel req
                else
                    Log.error $"responses api unable to reconnect aborting"
                    return raise ex
        }

    ///post request to respones api
    let sendRequest msgWrap replyChannel msg = 
        sendWithRetry 0 msgWrap replyChannel msg

    let hasFunction (resp:Response) =
        resp.output
        |> List.exists (fun x -> x.IsFunction_call)

    ///send an initial 'computer tool call' request
    let postStartCua replyChannel instructions (sanpshot,width,height,url,environment) chatHistory =
       async {
            let contImg = Input_image {|image_url = sanpshot|}
            let input = { Message.Default with content=[contImg]}
            let cuaTool = Tool_Computer_use {|display_height = height; display_width = width; environment = environment|}
            let req = {Request.Default with
                            input = [IOitem.Message input] @ (chatHistory |> List.map IOitem.Message)
                            tools= [cuaTool]
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

