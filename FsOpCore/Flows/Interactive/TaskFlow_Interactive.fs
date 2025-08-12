namespace FsOpCore.Interactive
open System
open System.Text.Json
open System.Threading
open Microsoft.SemanticKernel
open FsResponses
open FsOpCore
open FlUtils

module TaskFlow_Interactive =

    type SubState = {
        task                : TaskState<TaskFlowMsgIn,TaskFlowMsgOut>
        cts                 : CancellationTokenSource
        cuaLoopCount        : int
        reasonerLoopCount   : int
    }
        with 
            static member Create task = 
                let t = 
                            {
                                task                = task
                                cts                 = new CancellationTokenSource()
                                cuaLoopCount        = 0
                                reasonerLoopCount   = 0
                            }
                SubState.hookTaskTools t
                t

            member this.incrReasonerLoopCount()  = {this with reasonerLoopCount = this.reasonerLoopCount + 1}
            member this.incrCuaLoopCount()  = {this with cuaLoopCount = this.cuaLoopCount + 1}
            member this.resetCuaLoopCount() = {this with cuaLoopCount = 0}    

            static member hookTaskTools (ss:SubState) =
                let tasktools = ss.task.kernel.GetRequiredService<Functions.FsOpTaskTools>()
                tasktools.SetFunctions({Functions.TaskToolImpl.taskDone = fun ()->async{                    
                    Log.info $"Done call for task {ss.task.id}"
                    ss.task.bus.PostToFlow (W_Msg TaskFlowMsgIn.APi_TerminateTask)}}) //wire task tools plugin to this instance

    module States = 
        ///log that a message was ignored in some state
        let ignoreMsg s msg name =
            Log.warn $"{name}: ignored message {msg}"
            F(s,[])

        let reasonerRequestAndClear (ss:SubState) = 
            let req = {
                cuaPrompt = ss.task.cuaPrompt
                kernel = ss.task.kernel
                actions = ss.task.actionsString()
                items = ss.task.reasonerItems
                cuaMessages = ss.task.cuaMessages //think about this more
                toolDefs = ss.task.toolDefs
                memory = FlUtils.getMemory ss.task.kernel
            }
            let ss = {ss with task = ss.task.resetReasonerItems()} //clear any accumulated snapshots so they are not resent
            ss,req

        let cuaLoopRequest (ss:SubState) cc = 
            let instr =                
                [
                    Vars.memory, FlUtils.getMemory ss.task.kernel :> obj
                    Vars.steps, ss.task.steps |> Option.map Prompts.toJson |> Option.defaultValue "" :> obj
                ]
                |> Prompts.renderPrompt Cua_Interactive_Prompts.``cua loop``
            {CuaReq.Default with
                instructions = Some instr
                chatHistory = [] // this.task.cuaMessages |> FlResps.toMessages //think about this more               
                kernel = ss.task.kernel
                visualState = ss.task.visualState.Value
                computerCall = Some cc
            }

        let cuaStartRequest (ss:SubState) = 
            let instr =                
                [
                    Vars.memory, FlUtils.getMemory ss.task.kernel :> obj
                    Vars.steps, (ss.task.steps |> Option.map Prompts.toJson |> Option.defaultValue "") :> obj
                ]
                |> Prompts.renderPrompt Cua_Interactive_Prompts.``cua loop``
            //initial request has 'developer' prompt that is persistent
            let devPrompt = [Vars.cuaInstructions, ss.task.cuaPrompt :> obj] |> Prompts.renderPrompt Cua_Interactive_Prompts.``cua step start``
            let hist = [{Message.Default with content=[Content.Input_text {|text=devPrompt|}]; role="developer"}]
            {CuaReq.Default with
                instructions = Some instr
                chatHistory = hist
                kernel = ss.task.kernel
                visualState = ss.task.visualState.Value
            }

        let rec (|Txn|M|)  (s_ret,ss:SubState,msg) = //common message processing for each state
            match msg with
            | W_Err e                        -> Txn(F(s_terminate ss,[APo_Error e]))                               //error: switch to s_terminate; send error to app
            | W_Msg (AGi_Usage (id,usg))     -> let ss = {ss with task = ss.task.appendUsage (id,usg)}             //accumulate usage and return to same state
                                                Txn(F(s_ret ss, [APo_Usage ss.task.usage]))                        //  - also send usage to app
            | W_Msg APi_TerminateTask        -> Txn(F(s_terminate ss,[APo_Done ss.task]))                          //done: switch to s_terminate; send results to app
            | W_Msg APi_EndAndReport         -> let ss,req = reasonerRequestAndClear ss
                                                Txn(F(s_summarize ss, [RSNRo_Summarize req]))                      //req generate summary and switch s_summarizing
            | W_Msg msg                      -> M msg                                                              //to be handled by the state 

        and s_start ss msg = async {
            Log.info $"{nameof s_start}, {ss.cuaLoopCount}, {ss.reasonerLoopCount}, {ss.task.id}"
            match s_start,ss,msg with 
            | Txn s                         -> return s
            | M APi_Start                   -> let ss,req = reasonerRequestAndClear ss
                                               ss.task.bus.PostToAgent (RSNRo_GetSteps req)                        //ask reasoner to respond with steps (immediate)
                                               do! ss.task.driver.start ss.task.target                             //start browser - this takes some time
                                               let! vs = snapshot ss.task.driver                                   //take a snapshot
                                               let ss = {ss with task = ss.task.acceptVisualState vs}              //store snapshot
                                               let req = cuaStartRequest ss
                                               return F(s_cua ss,[CUAo_Req req])                                   //transition to s_cua state, send req to cua
            | x                             -> Log.warn $"{nameof s_start}: expecting APi_Start but got {x}"
                                               return !!(s_start ss)
        }
        
        and s_cua ss msg = async {
            Log.info $"{nameof s_cua}, {ss.cuaLoopCount}, {ss.reasonerLoopCount}, {ss.task.id}"
            match s_cua,ss,msg with 
            | Txn s                         -> return s
            | M (RSNRi_Steps steps)         -> let ss = {ss with task = ss.task.setSteps steps}.incrReasonerLoopCount() //update steps and count
                                               let ss,req = reasonerRequestAndClear ss
                                               return F(s_cua ss, [RSNRo_GetSteps req])                                 //txn back to s_cua; send new steps req
            | M (CUAi_ComputerCall cc)      -> let! task = ss.task.performActionAndCapture cc                           //handle computer call
                                               let ss = {ss with task=task}.incrCuaLoopCount()                          //incr cua loop count
                                               let req = cuaLoopRequest ss cc
                                               return F(s_cua ss,[CUAo_Req req; APo_Action (Actions.actionToString cc.action)]) //txn back to s_cua; send action to app and loop
            | M (CUAi_NoComputerCall s)     -> let ss = ss.resetCuaLoopCount() //no action requested (no computer call)
                                               let t = match s with Some s -> ss.task.prependCuaMessage (Assistant s) | None -> ss.task //append any cua message
                                               let ss = {ss with task = t}
                                               return F(s_pause ss,[APo_Paused ss.task.cuaMessages]) //txn 
            | x                             -> return ignoreMsg (s_cua ss) x (nameof s_cua)
        }

        and s_pause ss msg = async {
            Log.info $"{nameof s_pause}, {ss.cuaLoopCount}, {ss.reasonerLoopCount}, {ss.task.id}"
            match s_pause,ss,msg with 
            | Txn s                         -> return s
            | M (RSNRi_Steps steps)         -> let ss = {ss with task = ss.task.setSteps steps}.incrReasonerLoopCount() //update steps and count
                                               return !!(s_pause ss)
            | M (APi_Resume msg )           -> let t = ss.task.prependCuaMessage (User msg)
                                               let! vs = snapshot t.driver                                   //take a snapshot
                                               let ss = {ss with task = t.acceptVisualState vs}              //store snapshot
                                               let cuaReq = cuaStartRequest ss
                                               let ss,rsnrReq = reasonerRequestAndClear ss
                                               //transition to s_cua state, send req to cua, rsnr & app
                                               return F(s_cua ss,[CUAo_Req cuaReq; RSNRo_GetSteps rsnrReq; APo_Updated ss.task.cuaMessages]) 
            | x                             -> return ignoreMsg (s_pause ss) x (nameof s_pause)
        }

        and s_summarize ss msg = async {
            Log.info $"{nameof s_summarize}, {ss.cuaLoopCount}, {ss.reasonerLoopCount}, {ss.task.id}"
            match s_start,ss,msg with         
            | Txn s                         -> return s
            | M (RSNRi_Summary s)           -> let ss = {ss with task = ss.task.prependCuaMessage (Assistant s)}
                                               return F(s_terminate ss, [APo_Done ss.task ])
            | x                             -> return ignoreMsg (s_summarize ss) x (nameof s_summarize)
        }

        and s_terminate ss msg = async {
            Log.info $"{nameof s_terminate}, {ss.cuaLoopCount}, {ss.reasonerLoopCount}, {ss.task.id}"
            ss.cts.CancelAfter(1000)
            Log.info $"s_terminate: message ignored {msg}"
            return !!(s_terminate ss)
        }

    ///construct flow and also start it
    let create task : IFlow<TaskFlowMsgIn> =
        
        ///initial substate
        let ss0 = SubState.Create task

        //initial state
        let s0 = States.s_start ss0

        //start flow
        Workflow.run ss0.cts.Token task.bus s0

        //start companion agent
        CuaAgent.startCuaAgent task.bus
        ReasonerAgent.startReasonerAgent task.bus

        //return handler to talk to flow
        {new IFlow<TaskFlowMsgIn> with

            member _.Terminate () =
                async {
                    Log.info "terminating flow ..."
                    do! Async.Sleep(1000)
                    ss0.cts.Cancel()
                    ss0.task.bus.Close()
                }
                |> Async.Start

            member _.Post msg = task.bus.PostToFlow (W_Msg msg)
        }


