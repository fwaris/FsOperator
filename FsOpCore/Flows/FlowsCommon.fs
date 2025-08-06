namespace FsOpCore
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open System.Text.Json

type VisualState =
    {
        snapshot        : string
        width           : int
        height          : int
        url             : string option
        environment     : string
    }
    with static member Default =
                            {
                                snapshot    = ""
                                width       = C.VIEWPORT_WIDTH
                                height      = C.VIEWPORT_HEIGHT
                                url         = None
                                environment = ComputerEnvironment.browser
                            }

///type to package cua request parameters
type CuaReq =
    {
        instructions : string option
        visualState  : VisualState
        chatHistory  : Message list
        nonCuaTools  : Tool list
    }
    with static member Default =
                        {
                            instructions = None
                            visualState = VisualState.Default
                            chatHistory = []
                            nonCuaTools = []
                        }


module FlUtils =
    open System.Text.Json.Serialization
    ///utility operator to create default workflow states
    let (!!) s = F(s,[])

    let snapshot (driver:IUIDriver) = async {
        let! (snapshot,(w,h)) = driver.snapshot()
        let! url = driver.url()
        return {snapshot=snapshot; width=w; height=h; url=url;environment=driver.environment}
    }

    let parseMemory (memoryString:string) =
        try
            JsonSerializer.Deserialize<Map<string,string list>>(memoryString)
        with ext ->
            let lines =
                seq {
                    use rdr = new System.IO.StringReader(memoryString)
                    let mutable line = rdr.ReadLine()
                    while line <> null do
                        yield line
                        line <- rdr.ReadLine()
                }
                |> Seq.toList
            lines
            |> List.map (fun l -> l.Split(":"))
            |> List.map (fun xs -> if xs.Length = 1 then [|xs.[0];""|] else xs)
            |> List.map (fun xs -> xs.[0],xs.[1..] |> String.concat " ")
            |> List.groupBy fst
            |> List.map (fun (k,vs) -> k,vs |> Seq.map snd |> Seq.toList)
            |> Map.ofList

(*
let memoryString = "a:b\nc:d"
parseMemory memoryString
parseMemory ""
parseMemory ":"
parseMemory "a:"
parseMemory "a:b"
parseMemory "a:b:c"
*)


    ///extracts 'computer call' from response
    let computerCall (response:FsResponses.Response) =
        response.output
        |> List.choose (function
            | IOitem.Computer_call cb -> Some cb
            | _                -> None)
        |> List.tryHead

    ///returns true if no computer call present
    let noCC resp = (computerCall resp).IsNone

    ///log that a message was ignored in some state
    let ignoreMsg s msg name =
        Log.warn $"{name}: ignored message {msg}"
        F(s,[])

    ///convenience 'active pattern' to match a W_Reasoner msg
    ///with the given correlation id
    let (|Reasoner|_|) corrId msg =
        match msg with
        | W_Reasoner (id,resp) when id = corrId -> Some resp
        | _                                     -> None

    let hasFunction (resp:Response) =
        resp.output
        |> List.exists (fun x -> x.IsFunction_call)

    ///convenience 'active pattern' to match a W_Reasoner msg
    ///with at least one function call
    let (|FuncCall|_|)msg =
        match msg with
        | W_Reasoner (id,resp) when hasFunction resp -> Some (id,resp)
        | _                                          -> None

    ///convenience 'active pattern' to match a W_Reasoner msg
    ///with the given correlation id and with at least one function call
    let (|Cua_FuncCall|_|) = function
        | W_Cua (resp) when hasFunction resp -> Some resp
        | _                                  -> None

    ///Cua message with no computer call requested
    let (|NoComputerCall|_|) = function
        | W_Cua (resp) when noCC resp -> Some resp
        | _                           -> None

    let getUsage (resp:Response) = resp.model,resp.usage

    let getMemory (k:Kernel) =
        let svc = k.Services.GetService(typeof<Functions.FsOpMemory>)
        let mem =
            if svc = Unchecked.defaultof<_> then
                Map.empty
            else
                let svc = svc :?> Functions.FsOpMemory
                svc.getMemory()
        Functions.FsOpMemory.Serialize(mem)


    ///<summary>
    ///Json serialization options suitable for deserializing OpenAI 'structured output'.<br />
    ///Note: can use simple enums, in such types but not F# DUs
    ///</summary>
    let openAIResponseSerOpts =
        let o = JsonSerializerOptions(JsonSerializerDefaults.General)
        o.Converters.Add(JsonStringEnumConverter())
        o.WriteIndented <- true
        o.ReadCommentHandling <- JsonCommentHandling.Skip
        let opts = JsonFSharpOptions.Default()
        opts
            .WithSkippableOptionFields(true)
            .AddToJsonSerializerOptions(o)
        o

//utility functions for working Responses API messages
module FlResps =
    open FsResponses

    let temperature = 0.f

    let toMessages (chatMsgs:ChatMsg list) =
        chatMsgs
        |> List.map (function
            | ChatMsg.User m -> {id = None; role="user"; content = [Content.Input_text {| text = m |}]; status = None}
            | ChatMsg.Assistant m -> {id = None; role="assistant"; content = [Content.Output_text {text = m.content; annotations=None}] ; status = None}
            | ChatMsg.Developer m -> {id = None; role="developer"; content = [Content.Input_text {|text = m|}] ; status = None})

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

    ///extract any text message in response
    let extractText (response:FsResponses.Response) =
        RUtils.outputText response
        |> checkEmpty


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
    let rec private sendWithRetry count (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                return response
            with ex -> 
                match ex with 
                | :? NoFuncCallOuput as ex -> Log.info "Api was expecting function call output(s) which are not provided"
                | _                        -> ()                
                if count < 5 then
                    logApiException ex
                    do! Async.Sleep 3000
                    return! sendWithRetry (count + 1) req
                else
                    Log.error $"responses api unable to reconnect aborting"
                    return raise ex
        }

    ///post request to responses api
    let postRequestAndReplyToChannel msgWrap replyChannel req =
        async {
            let! response = sendWithRetry 0 req
            replyChannel (msgWrap response)
        }

    ///send a new cua request with 'computer tool call' - no prev state or history
    let postStartCuaRequest replyChannel cuaReq =
       let vs = cuaReq.visualState
       async {
            let contImg = Content.Input_image {|image_url = vs.snapshot|}
            let input = { Message.Default with content=[contImg]}
            let cuaTool = Tool.Computer_use {|display_height = vs.height; display_width = vs.width; environment = vs.environment|}
            let req = {Request.Default with
                            input = [IOitem.Message input] @ (cuaReq.chatHistory |> List.map IOitem.Message)
                            tools = cuaTool :: cuaReq.nonCuaTools
                            instructions = cuaReq.instructions
                            previous_response_id = None
                            store = true
                            tool_choice = ToolChoice.Required
                            temperature = temperature
                            reasoning = Some {Reasoning.Default with effort=Some Reasoning.Medium}
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            do! postRequestAndReplyToChannel W_Cua replyChannel req
        }
        |> catch replyChannel

