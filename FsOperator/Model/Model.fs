namespace rec FsOperator
open System
open System.Threading.Channels
open FsResponses
open FsOpCore

type FlowState = 
    | FL_Init 
    | FL_Flow of {| flow : IFlow<TaskFlowInteractive.TaskFlowMsgIn>; |}
    | FL_Paused of {| flow : IFlow<TaskFlowInteractive.TaskFlowMsgIn>; |}
    | FL_Flow_Summarizing of {| flow : IFlow<TaskFlowInteractive.TaskFlowMsgIn> |}

type Flow =
    {
        chat : Chat
        state :  FlowState
    }
    with 
        static member Default = {state = FL_Init; chat=Chat.Default}
        member this.messages() = this.chat.messages
        member this.Post msg = match this.state with FL_Flow f | FL_Flow_Summarizing f | FL_Paused f  -> f.flow.Post msg | _ -> ()
        member this.isRunning() = not this.state.IsFL_Init 
        member this.setChat ch = {this with chat = ch}        
        member this.setChatMsgs msgs = {this with chat.messages = msgs}
        member this.pause() = match this.state with FL_Flow f -> {this with state = FL_Paused f} | _ -> this
        member this.isPaused() = this.state.IsFL_Paused
        member this.setQuestion q = {this with chat.question = Some q}
        member this.resume() = 
            match this.state with 
            | FL_Paused f when this.chat.question.IsSome -> this.Post (TaskFlowInteractive.TFi_Resume this.chat.question.Value)
                                                            {this with state = FL_Flow f}
            | _                                          -> this
        member this.stopAndSummarize() = 
            match this.state with 
            | FL_Paused f 
            | FL_Flow f -> this.Post TaskFlowInteractive.TFi_EndAndReport
                           {this with state = FL_Flow_Summarizing f}
            | x         -> this
        member this.Terminate () = 
            match this.state with 
            | FL_Paused f
            | FL_Flow f 
            | FL_Flow_Summarizing f -> f.flow.Terminate(); {this with state = FL_Init}
            | x -> this


type Model = {
    plan        : OPlan option
    ui          : UserInterface
    driver      : IUIDriver
    opTask      : OpTask
    isDirty     : bool
    mailbox     : Channel<ClientMsg>
    log         : string list
    action      : string
    statusMsg   : (DateTime option*string)
    isFlashing  : bool
    flow        : Flow
    voiceAsst   : RTOpenAI.Api.Connection option    
}
    with member this.post msg = this.mailbox.Writer.TryWrite msg |> ignore

type ClientMsg =

    | OpTask_SetTextInstructions of string
    | OpTask_Update of OpTask
    | OpTask_SetTarget of string
    | OpTask_MarkDirty
    | OpTask_ClearDirty
    | OpTask_Load
    | OpTask_LoadSample of OpTask
    | OpTask_Loaded of OpTask option
    | OpTask_Save
    | OpTask_SaveAs
    | OpTask_Clear
    | OpTask_Saved of OpTask option
    
    | ToggleVoiceMode 

    | Flow_UpdateQuestion of string
    | Flow_StartStop
    | Flow_Terminate
    | Flow_StopAndSummarize
    | Flow_Resume
    | Flow_Msg of TaskFlowInteractive.TaskFlowMsgOut

    | Action_Set of string
    | Action_Flash of bool

    | Log_Append of string
    | Log_Clear

    | StatusMsg_Set of string
    | StatusMsg_Clear of DateTime option
    | SyncUrlToBrowser of bool //start browser
    | Error of exn
    | Abort of (exn option*string)
    | Nop of unit
    | TestSomething

    | Plan_Edit
    | Plan_Set of OPlan option

