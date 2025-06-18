namespace FsOpCore
open System
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open FlUtils
open System.Text.Json
open System.Text.Json.Schema

module PlanFlow =
    let MAX_SNAPSHOTS = 3
    let MAX_REASONER_STATE = 6
    
    ///flow input messages
    type PlanFLowMsgIn =
        | TFi_Start 
        | TFi_Resume of string
        | TFi_EndAndReport

    ///flow output messages
    type PlanFLowMsgOut =
        | TFo_Paused of ChatMsg list
        | TFo_Error of WErrorType
        | TFo_Action of string
        | TFo_Done of TaskState

    and TaskState = {
        cuaMessages     : ChatMsg list
        cuaPrompt       : string
        reasonerState   : IOitem list
        reasonerPrompt  : string option
        tools           : Tool list
        kernel          : Kernel
    }
    with 
        static member Create cuaPrompt reasonerPrompt tools kernel = 
                            {
                               cuaMessages = []
                               cuaPrompt = cuaPrompt
                               reasonerPrompt = reasonerPrompt
                               tools = tools
                               kernel = kernel
                               reasonerState = []
                            }
        member this.prependCuaMessage msg = {this with cuaMessages = msg::this.cuaMessages}
        member this.prependReasonerState items = {this with reasonerState = items @ this.reasonerState |> List.truncate MAX_REASONER_STATE}

    type SubState = {
        cts          : CancellationTokenSource
        task         : TaskState
        driver       : IUIDriver       
        bus          : WBus<PlanFLowMsgIn,PlanFLowMsgOut>
        actions      : string list
    }
        with 
            member this.prependCuaMessage msg = {this with task = this.task.prependCuaMessage msg}
            member this.prependAction a = {this with actions = a::this.actions |> List.truncate MAX_SNAPSHOTS }
            member this.prependReasonerState items = {this with task = this.task.prependReasonerState items}
            member this.prependSnapshot snapshot = 
                let imageCntnt = Content.Input_image {|image_url = snapshot|}
                [IOitem.Message {Message.Default with content = [imageCntnt]}] |> this.prependReasonerState
            member this.actionsString() = 
                this.actions 
                |> List.rev 
                |> List.indexed 
                |> List.map (fun (i,x) -> $"{i}: {x}") 
                |> String.concat ","

    //functions for Reasonser model
    module Rsnr = 
        type CuaContinueResponse = {
            cua_should_continue : bool
            message : string
        }

        type CuaInstructionsResponse = {
            cua_achieved_task : bool
            cua_guidance : string
        }

        ///call an indivudal function
        let invokeFunction (kernel:Kernel) (name:string) (arguments:string) = async {
            let args = JsonSerializer.Deserialize<Map<string,obj>>(arguments)
            let args = args |> Map.toSeq |> Prompts.kernelArgs          
            let! rslt = kernel.InvokeAsync(pluginName=null,functionName=name,arguments=args) |> Async.AwaitTask
            let rsltStr = JsonSerializer.Serialize(rslt)
            return rsltStr
        }
        
        ///handle all the functions calls found response message
        let callFunctions ss resp = async {
            let fns = 
                resp.output 
                |> List.choose (function 
                    | IOitem.Function_call fn -> Some fn
                    | _                       -> None)
            let mutable fouts = []
            for f in fns do
                let! rslt = invokeFunction ss.task.kernel f.name f.arguments
                let fout = IOitem.Function_call_output {call_id = f.call_id; output = rslt}
                fouts <- fout::fouts               
            let ss = ss.prependReasonerState fouts
            return ss
        }

        //
        let structuredFormat (t:Type) = 
            let opts = JsonSerializerOptions.Default
            let schema = opts.GetJsonSchemaAsNode(t)        
            {format = Json_schema {|name=t.Name; schema=schema; strict=true|}}


        ///<summary>
        ///Send a request to the reasoner model with the give correlationId (returned in response).<br />
        ///The request 'input' items are obtained from <see cref="TaskState.reasonerState" />
        ///</summary>
        let postToReasoner correlationId (ss:SubState) (responseFormat : Type option) reasonerInstructions  =
            async {
                let req = {Request.Default with
                                    input = List.rev ss.task.reasonerState
                                    instructions = reasonerInstructions
                                    store = false
                                    model=Models.gpt_41
                                    text = responseFormat |> Option.map structuredFormat
                                    truncation = Some Truncation.auto
                                    metadata = [C.CORR_ID,correlationId] |> Map.ofList |> Some
                                }
                do! FlResps.sendRequest Workflow.ReasonerMsgWithCorrId ss.bus.PostInput req                                    
            }
            |> FlResps.catch ss.bus.PostInput

        ///cua may be stuck; use this to terminate early and summarize the progress the plan can continue
        let stopAndSummarize (ss:SubState) = 
            let id = newId()        
            Prompts.kernelArgs [Vars.taskInstructions,ss.task.cuaPrompt] 
            |> Prompts.renderPrompt Prompts.``cua early termination prompt``
            |> Some
            |> postToReasoner id ss None
            id

        ///send messaage to reasoner to get guidance for cua for the next action
        let getGuidanceForCuaNextAction ss reasonerPrompt = 
            let id = newId()
            async {
                let cuaMessageHistory = 
                    ss.task.cuaMessages 
                    |> List.map (function
                        | User c -> $"user: {c}"
                        | Assistant m -> $"assistant: {m.content}")
                    |> String.concat System.Environment.NewLine
                let args = 
                    Prompts.kernelArgs 
                        [ 
                            Vars.cuaInstructions, ss.task.cuaPrompt
                            Vars.actionHistory,ss.actionsString()
                            Vars.cuaMessageHistory,cuaMessageHistory
                        ]
                let instructions = Prompts.renderPrompt reasonerPrompt args
                postToReasoner id ss (Some typeof<CuaInstructionsResponse>) (Some instructions)
            }
            |> FlResps.catch ss.bus.PostInput
            id

        ///ask reasoner to respond to cua as a user would, to continue cua after pause
        let getGuidanceAfterCuaPause ss = 
            let id = newId()
            async {
                let cuaMessageHistory = 
                    ss.task.cuaMessages 
                    |> List.map (function
                        | User c -> $"user: {c}"
                        | Assistant m -> $"assistant: {m.content}")
                    |> String.concat System.Environment.NewLine
                let args = 
                    Prompts.kernelArgs 
                        [ 
                            Vars.cuaInstructions, ss.task.cuaPrompt
                            Vars.actionHistory,ss.actionsString()
                            Vars.cuaMessageHistory,cuaMessageHistory
                        ]
                let instructions = Prompts.renderPrompt Prompts.``resume cua after pause`` args
                postToReasoner id ss (Some typeof<CuaContinueResponse>) (Some instructions)
            }
            |> FlResps.catch ss.bus.PostInput
            id

    //functions for Cua model
    module Cua = 
        ///if there is text content in resp then add that to chat history
        let prependAsstMsg (ss:SubState) resp = 
            FlResps.extractText resp 
            |> Option.map (fun text -> ss.prependCuaMessage (Assistant {id=resp.id; content=text}))
            |> Option.defaultValue ss

        ///if there is text content in resp then add that to chat history
        let prependUserMsg (ss:SubState) resp = 
            FlResps.extractText resp 
            |> Option.map (fun text -> ss.prependCuaMessage (User text))
            |> Option.defaultValue ss

        ///handle Cua respones to potentially perform a computer call
        let performComputerCall (ss:SubState) resp = async {
            //process computer call
            let! (ss,outMsgs,visualState) = 
                match FlResps.computerCall resp with
                | Some cb -> 
                    async {
                        do! Actions.doAction 2 ss.driver cb.action
                        let! visualState = snapshot ss.driver
                        let (snapshot,w,h,url,env) = visualState
                        let ss = ss.prependSnapshot snapshot //save screenshot for reasoner also
                        let actStr = Actions.actionToString cb.action
                        let ss = ss.prependAction actStr
                        return ss,[(TFo_Action actStr)],Some visualState
                    }
                | None -> async{ return ss,[],None }

            return ss,outMsgs,visualState
        }

        ///send the results of performing action to cua (along with optional additional guidance)
        let postCuaNext ss vs (cuaResp:Response) cuaInstr = 

            match vs, FlResps.computerCall cuaResp with 
            | Some (snapshot,w,h,url,env), Some cc ->            
                let cuaTool = Tool_Computer_use {|display_height = h; display_width = w; environment = env|}
                let cc_out = 
                    {
                        call_id = cc.call_id
                        acknowledged_safety_checks = FlResps.safetyChecks cuaResp
                        output = Computer_screenshot {|image_url = snapshot |}
                        current_url = url
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
                FlResps.sendRequest W_Cua ss.bus.PostInput req
            | None,_ -> async {return failwith "no 'visual state' e.g. sceenshot width, height, given"}
            | _,None -> async {return failwith "no computer call output found in response"}
            |> FlResps.catch ss.bus.PostInput

        ///resume with a new cua loop (after the old loop ended with no 'computer call')
        let postResumeCua ss snapshot =
            async {
                let chatHistory = FlResps.truncatedChatHistory ss.task.cuaMessages
                FlResps.postStartCua ss.bus.PostInput (Some ss.task.cuaPrompt) snapshot chatHistory
            }
            |> FlResps.catch ss.bus.PostInput 

    //state machine
    module States = 
        ///returns true if no compter call present
        let noCC resp = 
            let cc = FlResps.computerCall resp        
            cc |> Option.isNone


        ///log that a message was ignored in some state
        let ignoreMsg s msg name =
            Log.warn $"{name}: ignored message {msg}"
            F(s,[])

        ///convenience 'active pattern' to match a W_Reasoner msg
        ///with the given correlation id
        let (|Reasoner|_|) corrId msg = 
            match msg with 
            | W_Reasoner (id,resp) when id = corrId -> Some resp
            | _ -> None

        ///convenience 'active pattern' to match a W_Reasoner msg
        ///with the given correlation id and with at least one function call 
        let (|FuncCall|_|) corrId msg = 
            match msg with 
            | Reasoner corrId (resp) when FlResps.hasFunction resp -> Some resp
            | _                                     -> None

        let hasFn = FlResps.hasFunction

        (* --- states --- *)

        let rec s_start ss msg = async {    
            Log.info $"in s_start"
            match msg with 
            | W_Err e         -> return !!(s_terminate ss (Some e))
            | W_App TFi_Start -> let! (snapshot,w,h,url,env) = snapshot ss.driver
                                 let ss = ss.prependSnapshot snapshot //save for reasoner
                                 FlResps.postStartCua ss.bus.PostInput (Some ss.task.cuaPrompt) (snapshot,w,h,url,env) [] 
                                 return !!(s_loop ss)
            | x               -> Log.warn $"s_start: expecting {TFi_Start} message to start flow but got {x}"
                                 return !!(s_start ss)
        }

        and s_loop ss msg = async {
            Log.info $"in s_loop"
            match msg with 
            | W_Err e                    -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport     -> let corrId = Rsnr.stopAndSummarize ss
                                            return !!(s_summarizing ss corrId) 
            | W_Cua resp when noCC resp  -> let ss = Cua.prependAsstMsg ss resp    //cua not asking for comptuer call
                                            let corrId = Rsnr.getGuidanceAfterCuaPause ss 
                                            return !!(s_pause ss corrId)
            | W_Cua resp                 -> let ss = Cua.prependAsstMsg ss resp
                                            let! ss,outMsgs2,visualState = Cua.performComputerCall ss resp
                                            if ss.task.reasonerPrompt.IsSome then  //get reasoner guidance if prompt set
                                                let corrId = Rsnr.getGuidanceForCuaNextAction ss ss.task.reasonerPrompt.Value
                                                return F(s_reason ss (visualState,resp) corrId,outMsgs2)
                                            else 
                                                Cua.postCuaNext ss visualState resp None
                                                return F(s_loop ss,outMsgs2)
            | x                          -> return ignoreMsg (s_loop ss) x "s_loop"
        }            

        and s_reason ss (vs,cuaResp) corrId msg  = async {
            Log.info $"in s_reason"
            match msg with 
            | W_Err e                -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport -> let corrId = Rsnr.stopAndSummarize ss
                                        return !!(s_summarizing ss corrId) 
            | FuncCall corrId (resp) -> let ss = ss.prependReasonerState resp.output
                                        let! ss = Rsnr.callFunctions ss resp
                                        let corrId = Rsnr.getGuidanceForCuaNextAction ss ss.task.reasonerPrompt.Value //continue after func. calls
                                        return !!(s_reason ss (vs,cuaResp) corrId)
            | Reasoner corrId (resp) -> let ss = ss.prependReasonerState resp.output
                                        let resp = RUtils.parseContent<Rsnr.CuaInstructionsResponse> resp //get structured output
                                        match resp with 
                                        | None -> return failwith $"reasoner model did not send appropriate resp. for cua guidance"
                                        | Some (Choice2Of2 e) -> return failwith $"reaonser model refused to provide structured output '{e}'"
                                        | Some (Choice1Of2 cuaInstr) ->
                                            if cuaInstr.cua_achieved_task then 
                                                return F(s_terminate ss None, [TFo_Done ss.task])
                                            else
                                                Cua.postCuaNext ss vs cuaResp (Some cuaInstr.cua_guidance)
                                                return !!(s_loop ss)
            | x                      -> return ignoreMsg (s_reason ss (vs,cuaResp) corrId) x "s_reason"
        }

        and s_pause ss corrId msg = async {   
            Log.info $"in s_pause"
            match msg with 
            | W_Err e                -> return !!(s_terminate ss (Some e))
            | W_App TFi_EndAndReport -> let corrId = Rsnr.stopAndSummarize ss
                                        return !!(s_summarizing ss corrId) 
            | W_App (TFi_Resume tx)  -> let ss = ss.prependCuaMessage (User tx)
                                        let! (sn,_,_,_,_) as snapshot = FlUtils.snapshot(ss.driver)
                                        let ss = ss.prependSnapshot sn
                                        Cua.postResumeCua ss snapshot //resume chat with (note no previous history save on server)
                                        return !!(s_loop ss)
            | FuncCall corrId (resp) -> let ss = ss.prependReasonerState resp.output
                                        let! ss = Rsnr.callFunctions ss resp
                                        let corrId = Rsnr.getGuidanceAfterCuaPause ss 
                                        return !!(s_pause ss corrId)
            | Reasoner corrId (resp) -> let ss = ss.prependReasonerState resp.output
                                        let text = RUtils.outputText resp
                                        ss.bus.PostInput (W_App (TFi_Resume text))
                                        return !!(s_pause ss corrId)
            | x                       -> return ignoreMsg (s_pause ss corrId) x "s_pause"
        }

        and s_summarizing ss corrId msg = async {
            Log.info $"in s_summarizing"
            match msg with 
            | W_Err e                 -> return !!(s_terminate ss (Some e))
            | FuncCall corrId (resp)  -> let ss = ss.prependReasonerState resp.output
                                         let! ss = Rsnr.callFunctions ss resp
                                         let corrId = Rsnr.stopAndSummarize ss
                                         return !!(s_summarizing ss corrId)
            | Reasoner corrId (resp)  -> let ss = FlResps.extractText resp  
                                                  |> Option.map (fun txt -> ss.prependCuaMessage (Assistant {id=resp.id; content=txt})) 
                                                  |> Option.defaultValue ss
                                         return F(s_terminate ss None, [TFo_Done ss.task])
            | x                       -> return ignoreMsg (s_summarizing ss corrId) x "s_summarizing"
        }

        and s_terminate ss (e:WErrorType option) msg = async {
            Log.info $"in s_terminate"
            e 
            |> Option.iter (fun e -> 
                ss.bus.postOutput (TFo_Error e)
                Log.error (string e)
                ss.cts.CancelAfter(1000))
            Log.info $"s_terminate: message ignored {msg}"
            return !!(s_terminate ss None)
        }

    ///construct flow and also start it
    let create post driver task : IFlow<PlanFLowMsgIn> =
        let bus = WBus.Create<PlanFLowMsgIn,PlanFLowMsgOut> post

        ///initial substate
        let ss0 = {
            cts=new CancellationTokenSource()
            task = task
            bus=bus
            driver=driver
            actions = []        
        }

        //initial state
        let s0 = States.s_start ss0

        //start flow
        Workflow.run ss0.cts.Token bus s0

        //return handler to talk to flow
        {new IFlow<PlanFLowMsgIn> with

            member _.Terminate () =                 
                async {
                    Log.info "terminating flow ..."
                    do! Async.Sleep(1000)
                    ss0.cts.Cancel()
                    ss0.bus.Close()
                }
                |> Async.Start
 
            member _.Post msg = bus.PostInput (W_App msg)
        }

