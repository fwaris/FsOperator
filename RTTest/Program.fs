module Pgm
open Microsoft.SemanticKernel
open FsOpCore

module Samples = 

    let sample() = 
        let ln = 
            { OTask.Create() with
                target = OLink "https://jirasw.t-mobile.com/secure/Tempo.jspa#/my-work/timesheet?worker=JIRAUSER71672&dateDisplayType=days&periodType=FIXED&subPeriodType=MONTH&viewType=TIMESHEET&order=ASCENDING&sortBy=TITLE_COLUMN&columns=WORKED_COLUMN&groupBy=issue&from=2025-06-15&to=2025-06-21"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                cua = Some """Your task is to record my work hours from Jira’s Tempo for the week, capturing the required details for each task.

Required Fields for Each Task:
Task ID or Key: (e.g., AGAP-XXXX)

Daily Hours: Hours worked each day of the week for this task

Capability ID: (Starts with ‘CAP’)

How to Find the Capability ID:
Open Task Details:
Click on the task key to view task details.

Locate Parent Story:
At the top navigation links, find the sequence:
[Team Link] / [Story Link] / [Task Link]

Go to Story Page:
Click the Story Link to open the story.

Find Feature Link:
On the story page, click on the Feature Link.

Get Capability ID:
On the feature page, locate the Capability ID (it starts with “CAP”).

Store all collected data in memory for each task in Tempo. This information will be used for a later, downstream task.

Note you can use the 'back' button to go back, if lost.

Only gather the data needed. Make no other changes.
"""
                reasoner = Some Prompts.``reasoner prompt for cua guidance``            
                }
        let tw = 
            { OTask.Create() with
                target = OLink "https://www.twitter.com"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                description = "retrieve linkedIn people info from memory and get twitter handles"
                cua = Some """
list of names and linked in profile links. Search each name on twitter and obtain their
twitter handle. 
Use save_memory function to save each person's linked-in and twitter data
    """        
                reasoner = Some Prompts.``reasoner prompt for cua guidance``            
            }
        let plan = 
            { OPlan.Default with
                description = "take linkedin people and find their twitter handle"
                root = ONode.All {nodes= [ONode.One ln; ONode.One tw]; description=None}
            }
        plan

let s1 = Samples.sample()

//FsResponses.Log.debug_logging <- true
let kernel = 
    let b = Kernel.CreateBuilder()
    b.Plugins.AddFromType<OPlanMemory>() |>  ignore
    b.Build()


//kernel.Plugins.GetFunctionsMetadata() |> Seq.iter (fun x-> printfn "%s.%s" x.PluginName x.Name)
//kernel.Plugins.GetFunction("OPlanMemory", "save_memory")
let s1r = OPlanRun.Create s1 kernel

let t1 = OPlan.step s1r |> Async.RunSynchronously

for m in t1.currentTask.Value.messages do   
    printfn "%A" m

let i = 1