namespace FsOpCore
open Microsoft.SemanticKernel
open Microsoft.SemanticKernel.Plugins.Core

///Names of variables used in prompt templates
module Vars =
    let cuaInstructions = "cuaInstructions"
    let cuaMessageHistory = "cuaMessageHistory"
    let actionHistory = "actionHistory"
    let taskInstructions = "taskInstructions"
    let steps = "steps"
    let memory = "memory"
    let startUrl = "startUrl"
    let currentStep = "stepCurrentTask"
    let taskSteps = "stepAllTasks" 

///a collection of default prompts for various uses and some prompt utilities
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



    //a modification of OAI sample: see https://github.com/openai/openai-testing-agent-demo
    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.currentStep" /><br />
    /// - <see cref="Vars.taskSteps" />
    /// - <see cref="Vars.memory" />
    ///</summary>
    let ``cua prompt`` = $"""
You will be given a list of instructions with steps to operate a web application. 
You will need to navigate the web application and perform the actions described in the [CURRENT_STEP].
The complete steps in the current task are given under [TASK_STEPS].

Try to accomplish the [CURRENT_STEP] in the simplest way possible.
Once you believe your are done with all the tasks required or you are blocked and cannot progress
(for example, you have tried multiple times to accomplish a task but keep getting errors or blocked),
use the task_done tool to let the user know you have finished the task.
You do not need to authenticate on user's behalf, the user will authenticate and your flow starts after that.`;

## Memory:
You can read/write from/to memory using the functions provided to save relevant facts for later tasks.
However, any existing memory saved before this task is already provided in [MEMORY_CONTENTS].
Use the memory functions to save any additional content as per [TASK_STEPS].

# [CURRENT_STEP]
{{{{${Vars.currentStep}}}}}

# [TASK_STEPS]
{{{{${Vars.taskSteps}}}}}

# [MEMORY_CONTENTS]
{{{{${Vars.memory}}}}}

"""

    //a modification of OAI sample: see https://github.com/openai/openai-testing-agent-demo
    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.currentStep" /><br />
    /// - <see cref="Vars.taskSteps" />
    /// - <see cref="Vars.memory" />
    ///</summary>
    let ``review steps`` = $"""
Your job is to review the steps and the accompanying screenshots to determine which of the steps have been completed.

# Schema of the Step
type Status =  ToDo = 0 | Done = 1
type CuaInstructionStep =
{{ 
    step_num: int
    step_instructions: string
    step_status : Status
}}

[STEPS]
{{{{${Vars.steps}}}}}

 Do not add or remove any steps. Do not modify any step that already has a "Done" status. Keep "ToDo" steps as needed. 
  Keep the same step_number order.
"""

    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.cuaInstructions" /><br />
    /// - <see cref="Vars.actionHistory" />
    /// - <see cref="Vars.cuaMessageHistory" />
    /// - <see cref="Vars.memory" />
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
**Only provide the immediate next step to help the CUA continue.** Do not issue multi-step instructions.
For example, to enter text into a field, ask CUA first to click in or focus the field.
Wait to make sure the cursor is blinking in that field. Then issue the *type* <text> instructions.
In the next snapshot ensure the text was  actually entered.
Review the latest snapshot image after CUA action and issue the next instruction accordingly.
*Don't assume that CUA has actually followed through*.
CUA may delay following instructions so they may have to be repeated.
Note: Commands like 'snapshot' and 'wait' don't take actions on the page.
If you think, CUA is not following instructions, try to issue them in all caps (expect for literal text to be entered)

## Miscellaneous:
CUA does not have the ability to call functions. Instead of asking CUA to invoke functions, you just invoke the functions directly.
To save and retrieve memory, use the functions provided.
Extract relevant textual information from the screenshots images provided and save to memory if needed
CUA cannot focus on the browser's address bar; to get the browser page url use the 'get_url' function.

## Memory:
You can read/write from/to memory using the functions provided to save relevant facts for later tasks.
However, any existing memory saved before this task is already provided in [MEMORY_CONTENTS].

Assume that CUA only has access to a Web Brower (not the whole computer).

## Termination
**Check to make sure that all steps of the Task are done.**
If the task is complete, respond accordingly.

# [TASK_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

# [CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

# [ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}

# [MEMORY_CONTENTS]
{{{{${Vars.memory}}}}}

Today is {{time.today}}

"""

    ///<summary>
    ///Template variables: <br />
    /// - <see cref="Vars.cuaInstructions" /><br />
    /// - <see cref="Vars.actionHistory" />
    /// - <see cref="Vars.cuaMessageHistory" />
    /// - <see cref="Vars.memory" />
    ///</summary>
    let ``resume cua after pause`` = $"""
The Computer Use Agent (CUA) follows a set of instructions [CUA_INSTRUCTIONS] to complete a task by issuing commands like click, move, or type text based on screenshots.

The CUA models has moved through multiple turns but now not issued a new command, indicating
that it might be done.

Your task:
Review the [CUA_MESSAGE_HISTORY]; [ACTION_HISTORY]; [MEMORY_CONTENT] the previous screenshots in the context; and determine if the
task as stated in [CUA_INSTRUCTIONS] has been accomplished.

If the task has not be accomplished, issue brief instructions so that cua an continue forward to accomplish the task.

[CUA_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

[ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}

[MEMORY_CONTENTS]
{{{{${Vars.memory}}}}}

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

    ///<summary>
    ///Template variables:<br />
    /// <see cref="Vars.taskInstructions" /><br />
    /// <see cref="Vars.steps" /><br />
    /// <see cref="Vars.memory" />
    ///</summary>
    let ``cua early termination prompt step`` = $"""The user has tasked an automated 'computer assistant agent' (CUA)
to accomplish a task as given in the high-level [TASK_INSTRUCTIONS]. The computer
assistant has operated the computer in pursuit of the task and the results are presented here.

The high-level [TASK_INSTRUCTIONS] were broken down into smaller steps. The list of steps,
and their statuses are given in [STEPS-BY-STEP_INSTRUCTIONS], which also contains any message history captured when the step was run.

The current task and (any previous tasks) had access to a common 'memory' area to which the tasks may have written some content.
The contents of the memory are are given in [MEMORY_CONTENT].

Along the way, CUA took some screenshots which are also attached.

Given the [TASK_INSTRUCTIONS] and the supporting content, determine how much of the task was completed and provide any results obtained.

[TASK_INSTRUCTIONS]
{{{{${Vars.taskInstructions}}}}}

[STEPS-BY-STEP_INSTRUCTIONS]
{{{{${Vars.steps}}}}}

[MEMORY_CONTENTS]
{{{{${Vars.memory}}}}}
"""

    ///<summary>
    /// Variables: <see cref="Vars.taskInstructions" /><br />
    /// Variables: <see cref="Vars.startUrl" />
    ///</summary>
    let ``starting voice prompt`` = $"""You are to collaborate with a user to help complete a task.
The task is actually performed by a separate 'AGENT'.
The AGENT has the capability to perform computer actions if instructed.
**YOU CAN ASK THE AGENT TO GOTO WEB PAGES AND PERFORM ACTIONS ON THEM.**
**Use the function 'voice_gotoUrl' to ask the AGENT to go to a specific URL.**
For example, the AGENT can open web pages and browse through them to get infomation.
Use the supplied tools and functions to instruct the AGENT to perform actions.

There are three parties involved:
You : The AI Assistant
AGENT : The computer assistant that performs the given instructions
User : The human user who is interacting with you

The starting instructions for the AGENT, if any, are given in [TASK_INSTRUCTIONS].
The start URL, if any, is given in [START_URL].

# If no [START_URL] is given, collaborate with the user to establish the start and set it via 'voice_gotoUrl' function.

You can follow user's direction to give CUA additional guiance by using the 'voice_addGuidance' function.

[TASK_INSTRUCTIONS]
{{{{${Vars.taskInstructions}}}}}

[START_URL]
{{{{${Vars.startUrl}}}}}

"""

    ///<summary>
    /// Variables: <see cref="Vars.cuaInstructions" /><br />
    ///</summary>
    let ``divide cua instructions into steps`` = $"""You are to look at a set of
instructions for a COMPUTER USE AGENT (CUA) task.
CUA has the capability to perform computer actions if instructed, e.g. goto web pages and take actions such as click, type, keystrokes, etc.
Look at the CUA instructions [TASK_INSTRUCTIONS] and divide these into more granular instructions, if required. 
Assume that the CUA is starting at the target page.

Stay true to the [TASK_INSTRUCTIONS].

Ask CUA to use memory tools so save and retrieve information - rather than using copy-paste.

[TASK_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

"""

