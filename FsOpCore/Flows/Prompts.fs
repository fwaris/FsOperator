namespace FsOpCore
open Microsoft.SemanticKernel

module Vars = 
    let cuaInstructions = "cuaInstructions"
    let cuaMessageHistory = "cuaMessageHistory"
    let actionHistory = "actionHistory"
    let taskInstructions = "taskInstructions"

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
            let k = Kernel.CreateBuilder().Build()
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
    let ``reasoner prompt for cua guidance orig`` = $"""
The 'computer use agent' (CUA) model is given instructions [CUA_INSTRUCTIONS] to accomplish a task.

CUA 'looks' at screenshots and issues computer commands such as, 'click', 'move', 'type text', etc. to achieve its goal.
However the CUA model is not good at following instructions sometimes. 
Look at the [CUA_MESSAGE_HISTORY] and [ACTION_HISTORY]; and generate additional guidance that 
may be provided to the CUA model *after* the current given command has been performed and *before* CUA is ready to generate the next command.

Note: Sometimes CUA has trouble performing scrolling using the simple 'scroll' command. If CUA seems stuck,
suggest alternative scroll commands e.g. 'wheel' and PAGEUP/PAGEDOWN keystrokes.

When asking CUA to enter text, suggest type <text> in the <field name>

Just give the immediate next step to follow. Dont' give multi-step instructions. 

BE BRIEF

[CUA_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

[ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}
"""

    ///<summary>
    ///variables: <br />
    /// - <see cref="Vars.cuaInstructions" /><br />
    /// - <see cref="Vars.actionHistory" />
    /// - <see cref="Vars.cuaMessageHistory" />
    ///</summary> 
    let ``reasoner prompt for cua guidance`` = $"""
The Computer Use Agent (CUA) follows a set of instructions [CUA_INSTRUCTIONS] to complete a task by issuing commands like click, move, or type text based on screenshots.

CUA may not always follow instructions accurately.

Your task:
Review the [CUA_MESSAGE_HISTORY]; [ACTION_HISTORY]; the previous screenshots in the context; and generate brief, single-step guidance that can be shown to the CUA after its most recent action and before it generates its next command.

Guidance rules:

If scrolling seems ineffective, suggest alternatives like 'wheel', 'PAGEUP', or 'PAGEDOWN'.

When suggesting text entry, use the format: type "<text>" in the <field name>.

Avoid multi-step instructions.

Be concise.

Only provide the immediate next step to help the CUA continue.

[CUA_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

[ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}
"""

    let ``resume cua after pause`` = $"""
The Computer Use Agent (CUA) follows a set of instructions [CUA_INSTRUCTIONS] to complete a task by issuing commands like click, move, or type text based on screenshots.

The CUA models has moved through multiple turns but now not issued a new command, indicating 
that it might be done.

Your task:
Review the [CUA_MESSAGE_HISTORY]; [ACTION_HISTORY]; the previous screenshots in the context; and determine if the 
task as stated in [CUA_INSTRUCTIONS] has been accomplised.

If the task has not be accomplished, issue brief instructions so that cua an continue forward to accomplish the task.


[CUA_INSTRUCTIONS]
{{{{${Vars.cuaInstructions}}}}}

[CUA_MESSAGE_HISTORY]
{{{{${Vars.cuaMessageHistory}}}}}

[ACTION_HISTROY]
{{{{${Vars.actionHistory}}}}}
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

