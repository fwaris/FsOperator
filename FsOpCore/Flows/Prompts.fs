namespace FsOpCore
open Microsoft.SemanticKernel
open Microsoft.SemanticKernel.Plugins.Core

module Vars =
    let cuaInstructions = "cuaInstructions"
    let cuaMessageHistory = "cuaMessageHistory"
    let actionHistory = "actionHistory"
    let taskInstructions = "taskInstructions"
    let startUrl = "startUrl"

///a collection of default prompts for various uses and some prompt utilites
module Prompts =

    ///create a KernelArguments instance which holds the
    ///values for prompt template variable names
    let kernelArgs (args:(string*obj) seq) =
        let sttngs = PromptExecutionSettings()
        let kargs = KernelArguments(sttngs)
        for (k,v) in args do
            kargs.Add(k,v)
        kargs

    ///render a prompt template by replacing
    ///variable place holders in the template
    ///with the values held in the given KernelArguments
    let renderPrompt (promptTemplate:string) (args:KernelArguments) =
        (task {
            let b = Kernel.CreateBuilder()
            b.Plugins.AddFromType<TimePlugin>("time") |> ignore
            let k = b.Build()
            let fac = KernelPromptTemplateFactory()
            let cfg = PromptTemplateConfig(template = promptTemplate)
            let pt = fac.Create(cfg)
            let! rslt = pt.RenderAsync(k,args) |> Async.AwaitTask
            return rslt
        }).Result //async not needed as all local

    ///<summary>
    ///variables: <br />
    /// - <see cref="Vars.cuaInstructions" /><br />
    /// - <see cref="Vars.actionHistory" />
    /// - <see cref="Vars.cuaMessageHistory" />
    ///</summary>
    let ``reasoner prompt for cua guidance`` = $"""
The Computer Use Agent (CUA) follows a set of instructions to complete a task by issuing commands like click, move, or type text based on screenshots.

CUA may not always follow instructions accurately.

Your task:
Drive CUA to accomplish the task described in [TASK_INSTRUCTIONS].

Review the [CUA_MESSAGE_HISTORY]; [ACTION_HISTORY]; the previous screenshots in the context; and generate brief, single-step guidance that can be shown to the CUA after its most recent action and before it generates its next command.

# GUIDANCE RULES

## Scrolling:
Generally, CUA has no issues with scrolling the whole page, i.e. when there is a single scroll bar. 
However, if there are multiple scroll bars then scrolling could be an issue. 
If you detect scrolling is an issue, suggest alternatives like 'wheel', 'PAGEUP', or 'PAGEDOWN'. 
You may also suggest moving the cursor to a particular location and then issuing scroll commands.

## Instruction Generation:
**Only provide the immediate next step to help the CUA continue.** Do not issue multi-step instrucitons.
For example, to enter text into a field, ask CUA first to click in or focus the field.
Wait to make sure the cursor is blinking in that field. Then issue the *type* <text> instructions. 
In the next snapshot ensure the text was  actually entered.
Review the latest snapshot image after CUA action and issue the next instruction accordinly.
*Don't assume that CUA has actually followed through*. 
CUA may delay following instructions so they may have to be repeated. 
Note: Commands like 'snapshot' and 'wait' don't take actions on the page.
If you think, CUA is not following instructions, try to issue them in all caps (expect for literal text to be entered)

## Miscellaneous:
CUA does not have the ability to call functions. Instead of asking CUA to invoke functions, you just invoke the functions directly.
To save and retrieve memory, use the functions provided.
Extract relevant textual information from the screenshots images provided and save to memory if needed
CUA cannot focus on the browser's address bar; to get the browser page url use the 'get_url' function.

## Termination
**Check to make sure that all steps of the Task are done.**
If the task is complete, respond accordingly.

# [TASK_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

# [CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

# [ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}

Today is {{time.today}}

"""

    let ``resume cua after pause`` = $"""
The Computer Use Agent (CUA) follows a set of instructions [CUA_INSTRUCTIONS] to complete a task by issuing commands like click, move, or type text based on screenshots.

The CUA models has moved through multiple turns but now not issued a new command, indicating
that it might be done.

Your task:
Review the [CUA_MESSAGE_HISTORY]; [ACTION_HISTORY]; the previous screenshots in the context; and determine if the
task as stated in [CUA_INSTRUCTIONS] has been accomplished.

If the task has not be accomplished, issue brief instructions so that cua an continue forward to accomplish the task.


[CUA_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

[ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}

Today is {{time.today}}
"""

    ///<summary>
    ///Variables: <see cref="Vars.taskInstructions" />
    ///</summary>
    let ``cua early termination prompt`` = $"""The user has tasked an automated 'computer assistant'
to accomplish a task as given in the TASK INSTRUCTIONS below. The computer
assistant has operated the computer in pursuit of the task. Along the way it has
taken some screenshots. Give any available message history and the screenshots, summarize the content
obtained thus far, in relation to the task instructions.

# TASK INSTRUCTIONS
{{{{${Vars.taskInstructions}}}}}
"""

    let ``starting voice prompt`` = $"""You are to collaborate with a user to help complete a task.
The task is actually performed by a separate 'AGENT'. 
The AGENT has the capability to perform computer actions if instructed.
**YOU CAN ASK THE AGENT TO GOTO WEB PAGES AND PERFORM ACTIONS ON THEM.**
**Use the function 'gotoUrl' to ask the AGENT to go to a specific URL.**
For example, the AGENT can open web pages and browse through them to get infomation.
Use the supplied tools and functions to instruct the AGENT to perform actions.

There are three parties involved:
You : The AI Assistant
AGENT : The computer assistant that performs the given instructions
User : The human user who is interacting with you

The starting instructions for the AGENT, if any, are given in [TASK_INSTRUCTIONS]. 
The start URL, if any, is given in [START_URL].

# CASE 1: [TASK_INSTRUCTIONS] provided:
You can start the task by invoking the 'startTask' function.

# CASE 2: [TASK_INSTRUCTIONS] empty:
In this case, converse with the user to generate task instructions. 
Once the instructions are complete and the User confirms it. Use the 'setInstructions' function to give them to the AGENT.
Note setInstructions will override any previously set instructions so evertime you invoke it, the old instructions will be replaced with new ones.
Once the instructions are set, use 'startTask' to tell the AGENT to start the task.

NOTE: Once that task is started the 'setInstructions' function will have no affect.

[TASK_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[START_URL]
{{{{${Vars.startUrl}}}}}

# Running Task Actions
If the task is running, you can converse with the user and issue additional guidance to the AGENT. 
Use the 'addGuidance' function to convey incremental guidance to the user.

"""