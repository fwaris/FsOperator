namespace FsOpCore.Interactive
open FsOpCore
open FsResponses
open Microsoft.SemanticKernel

//reasoner agent messages 
type FromReasonerAgent =     
    | RAi_Steps of CuaInstructionStep list

type ReasonerReq = {cuaPrompt:string; items:IOitem list; actions:string; cuaMessages:ChatMsg list; toolDefs:Function list; memory:string; kernel:Kernel}
    with static member Default = {cuaPrompt=""; items=[]; actions="";cuaMessages=[];toolDefs=[]; memory=""; kernel=Kernel.CreateBuilder().Build()}

///flow messages
and TaskFlowMsgIn =
    //app msgs into flow
    | APi_Start
    | APi_EndAndReport
    | APi_TerminateTask
    | APi_Resume of string
    | AFi_Prime
    | APi_Voice_SetUrl of string
    | AFi_Voice_AddGuidance of string
    //agent messages into flow
    | AGi_Usage of string*Usage
    | RSNRi_Steps of CuaInstructionStep list
    | RSNRi_Summary of string
    | CUAi_ComputerCall of FsResponses.ComputerCall
    | CUAi_NoComputerCall of string option

and TaskFlowMsgOut =
    //msgs from flow to app
    | APo_Error of WErrorType
    | APo_Action of string
    | APo_Paused of ChatMsg list
    | APo_Updated of ChatMsg list
    | APo_Usage of Map<string,FsResponses.Usage list>
    | APo_Done of TaskState<TaskFlowMsgIn,TaskFlowMsgOut>
    | APo_Log of string
    //messages from flow to agents
    | RSNRo_GetSteps of ReasonerReq
    | RSNRo_Summarize of ReasonerReq 
    | CUAo_Req of CuaReq
    with override this.ToString() =
            match this with
            | APo_Error _ -> "APo_Error"
            | APo_Action _ -> "APo_Action"
            | APo_Done _ -> "APo_Done"
            | APo_Usage _ -> "APo_Usage"
            | APo_Paused _ -> "APo_Paused"
            | APo_Updated _ -> "APo_Updated"
            | RSNRo_GetSteps _ -> "RSNRo_GetSteps"
            | RSNRo_Summarize _ -> "RSNRo_Summarize"
            | CUAo_Req _ -> "CUAo_Req"