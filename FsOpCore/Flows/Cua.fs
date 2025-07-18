namespace FsOpCore
open FsResponses

//functions for Cua model
module Cua =
    ///if there is text content in resp then add that to chat history as asst. msg, also return the text
    let prependAsstMsg (task:TaskState<_,_>) resp =
        FlResps.extractText resp
        |> Option.map (fun text -> task.prependCuaMessage (Assistant {id=resp.id; content=text}),Some text)
        |> Option.defaultValue (task,None)

    ///if there is text content in resp then add that to the step chat history as asst. msg, also return the text
    let stepPrependAsstMsg (task:TaskState<_,_>) resp =
        FlResps.extractText resp
        |> Option.map (fun text -> {task with steps = task.steps.PrependCuaMessage (Assistant {id=resp.id; content=text})},Some text)
        |> Option.defaultValue (task,None)

    ///if there is text content in resp then add that to chat history as user msg
    let prependUserMsg (task:TaskState<_,_>) resp =
        FlResps.extractText resp
        |> Option.map (fun text -> task.prependCuaMessage (User text))
        |> Option.defaultValue task

    ///if there is text content in resp then add that to the step chat history as user msg
    let stepPrependUserMsg (task:TaskState<_,_>) resp =
        FlResps.extractText resp
        |> Option.map (fun text -> {task with steps = task.steps.PrependCuaMessage (User text)})
        |> Option.defaultValue task

    ///handle Cua respones to potentially perform a computer call
    let performComputerCall task resp = async {
        //process computer call
        let! (ss,visualState) =
            match FlUtils.computerCall resp with
            | Some cb ->
                async {
                    do! Actions.doAction 2 task.driver cb.action
                    let! visualState = FlUtils.snapshot task.driver
                    let task = task.prependSnapshot visualState.snapshot //save screenshot for reasoner also
                    let actStr = Actions.actionToString cb.action
                    let task = task.prependAction actStr
                    return task,Some visualState
                }
            | None -> async{ return task,None }

        return ss,visualState
    }

    ///handle all the functions calls found in the response message
    let callFunctions task resp = async {
        let fns =
            resp.output
            |> List.choose (function
                | IOitem.Function_call fn -> Some fn
                | _                       -> None)
        let mutable fouts = []
        for f in fns do
            let! rslt = FlUtils.invokeFunction task.kernel f.name f.arguments
            let fout = IOitem.Function_call_output {call_id = f.call_id; output = rslt}
            fouts <- fout::fouts
        return fouts
    }

    ///send the function call results back to CUA model
    let postCuaFuncResults ss (cuaResp:FsResponses.Response) fnouts =
        async {
            let req = {Request.Default with
                            input = fnouts
                            previous_response_id = Some cuaResp.id
                            store = true
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            do! FlResps.postRequestAndReplyToChannel W_Cua ss.bus.PostInput req
        }
        |> FlResps.catch ss.bus.PostInput


    ///send the results of performing action to cua (along with optional additional guidance)
    let postCuaNext ss vs (cuaResp:Response) cuaInstr =

        match vs, FlUtils.computerCall cuaResp with
        | Some vs, Some cc ->
            let cuaTool = Tool_Computer_use {|display_height = vs.height; display_width = vs.width; environment = vs.environment|}
            let cc_out =
                {
                    call_id = cc.call_id
                    acknowledged_safety_checks = FlResps.safetyChecks cuaResp
                    output = Computer_screenshot {|image_url = vs.snapshot |}
                    current_url = vs.url
                }
                |> IOitem.Computer_call_output
            let input =
                match cuaInstr with
                | Some text ->
                    Log.info $"Reasoner guidance: `{text}`"
                    let textMsg = {Message.Default with content = [Content.Input_text {|text=text|}]}
                    [cc_out;IOitem.Message textMsg]
                | None -> [cc_out]
            let req = {Request.Default with
                            input = input; tools=[cuaTool]
                            previous_response_id = Some cuaResp.id
                            store = true
                            model=Models.computer_use_preview
                            truncation = Some Truncation.auto
                        }
            FlResps.postRequestAndReplyToChannel W_Cua ss.bus.PostInput req
        | None,_ -> async {return failwith "no 'visual state' e.g. sceenshot width, height, given"}
        | _,None -> async {return failwith "no computer call output found in response"}
        |> FlResps.catch ss.bus.PostInput


    ///resume with a new cua loop (after the old loop ended with no 'computer call')
    let postResumeCua task snapshot =
        async {
            let chatHistory = FlResps.truncatedChatHistory task.cuaMessages
            FlResps.postStartCua task.bus.PostInput {CuaReq.Default with instructions=(Some task.cuaPrompt); visualState=snapshot; chatHistory=chatHistory}
        }
        |> FlResps.catch task.bus.PostInput

    ///resume with a new cua loop (after the old loop ended with no 'computer call'), for the current step
    let postResumeCuaStep task snapshot =
        async {
            let chatHistory = FlResps.truncatedChatHistory (task.steps.CurrentStep() |> Option.map (fun s->s.cuaMessages) |> Option.defaultValue [])
            let instruction = task.steps.CurrentInstruction()
            FlResps.postStartCua task.bus.PostInput {CuaReq.Default with instructions=Some instruction; visualState=snapshot; chatHistory=chatHistory}
        }
        |> FlResps.catch task.bus.PostInput


