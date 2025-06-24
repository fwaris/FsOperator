module Pgm
open Microsoft.SemanticKernel
open FsOpCore

module Samples =

    let sample() =
        let tHours =
            { OTask.Create() with
                id = "load_hours"
                description = "save task and daily hours from jira"
                target = OLink "https://t-mobile.atlassian.net/projects/AGAP?selectedItem=com.atlassian.plugins.atlassian-connect-plugin:is.origo.jira.tempo-plugin__tempo-project-centric-timesheet-panel"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """Your task is to record task ids and associated daily hours
from Jira’s Timesheet view for the current week

Select the current week from the time selector.

Note down the task id, date and hours and save them to memory.

Extract the information from the screenshots provided and invoke the save_memory function to save the data into memory.

**ALL information you need should all be available on a SINGLE PAGE. Don't click links to go to other pages.**
*If a horizontal or vertical scroll bar is visible for the Timesheet view*, scroll appropriately to see all data.

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.

End the task when the relevant data has been saved.
"""
                }

        let tCapability =
            { OTask.Create() with
                id = "get_capability_ids"
                description = "get capability ids for each task in jira"
                target = OLink "https://t-mobile.atlassian.net/projects/AGAP?selectedItem=com.atlassian.plugins.atlassian-connect-plugin:is.origo.jira.tempo-plugin__tempo-project-centric-timesheet-panel"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """Retrieve the task ids and hours from memory that were saved by a previous task.
Your goal is to record the Capability ID: (Starts with ‘CAP’) for each task id

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

Collect and save Capability ID

End task when all Capability IDs have been saved

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.
"""
                }
        let t_tTime =
            { OTask.Create() with
                id = "enter_hours_into_t-time"
                target = OLink "https://apps.powerapps.com/play/e/7ccae0f5-3b24-4e97-a2a1-0171636f64ff/a/e9ecf476-d164-41f6-b24b-84d14f4a3b6f"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                description = "Enter capability hours into T-Time"
                cua = Some """Retrieve the Capability Ids and hours from memory.
    Calculate the total hours for each Capability for each day.
    Enter the search for the capability in "Capability" search box.
    If the capability exist the enter the hours for each day for that capability.
    Save the T-Time hours.
    Do not "Submit", just "Save".
    """
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
            }
        let plan =
            { OPlan.Default with
                description = "Take hours from jira and enter them into t-time"
//                root = ONode.All {nodes= [ONode.One tHours; ONode.One tCapability; ONode.One t_tTime]; description=None}
                root = ONode.All {nodes= [ONode.One tCapability; ONode.One t_tTime]; description=None}
            }
        plan

let s1 = Samples.sample()

//FsResponses.Log.debug_logging <- true
let kernel planRef =
    let b = Kernel.CreateBuilder()
    let mem = OPlanMemory()
    mem.save_memory("AGAP-8515", "22/Jun/25: 0; 23/Jun/25: 8; 24/Jun/25: 8; 25/Jun/25: 0; 26/Jun/25: 0; 27/Jun/25: 0; 28/Jun/25: 0") |> ignore
//    b.Plugins.AddFromObject(OPlanMemory.LoadState()) |> ignore
    b.Plugins.AddFromObject(mem) |> ignore
    b.Plugins.AddFromObject(Navigator(planRef)) |> ignore
    b.Build()

//holder for runtime plan (provides context for some function calls)
let planRef = ref Unchecked.defaultof<_>

//kernel.Plugins.GetFunctionsMetadata() |> Seq.iter (fun x-> printfn "%s.%s" x.PluginName x.Name)
//kernel.Plugins.GetFunction("OPlanMemory", "save_memory")
let s1r = OPlanRun.Create s1 (kernel planRef)

//let t1 = OPlan.run s1r |> Async.RunSynchronously
let s2r = OPlan.run planRef s1r |> Async.RunSynchronously

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1

