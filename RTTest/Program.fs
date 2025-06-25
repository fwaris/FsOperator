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
                cua = Some """Your goal is to save the Capability ID for each jira task

First, retrieve jira task ids from memory.

For each jira task id (that has hours) do the following:
1. Task Details: Use search box to locate the task details page
2. Parent Story: Breadcrumbs at top: [...] / [Story Link] / [Task Link]; use Story Link
3. Parent Feature: Breadcrumbs at top: [...] / [Feature Link] / [Story Link]; use Feature Link
4. Capability Id : On Feature Page Look for the Capability Id (starts with 'CAP')
5. Save capability id for each jira task id into memory
6. Use 'home' function to get back to the home page
7. Repeat 1. to 6. for the next jira task with hours, if any

**End the CUA task when the Capability IDs for all tasks have been saved.**

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.
"""
                }
        let t_tTime =
            { OTask.Create() with
                id = "enter_hours_into_t-time"
                target = OLink "https://apps.powerapps.com/play/e/7ccae0f5-3b24-4e97-a2a1-0171636f64ff/a/e9ecf476-d164-41f6-b24b-84d14f4a3b6f"
                tools = FlUtils.makeFunctionTools<OPlanMemory>()
                description = "Enter capability hours into T-Time"
                cua = Some """Your goal is to add rows for each Capability ID, found in memory, for the selected week. The row contains hours
for each weekday for jira tasks related to the capability.

Retrieve Capability and jira task data from memory.

**Select the date range on the page that matches the date range of jira tasks found in memory.**

For each Capability ID in memory do:
1. Select the Capability by entering the Capability ID in the 'Capability' box 
1.1 Then select the Capability name that shows in the filtered list
2. Select "NEW Functionality: Application Coding" for 'Activity Id'
3. Add row
4. Enter hours for each day
5. Repeate 1. to 5. for the next capability, if any

Note: to enter hours click in the hours field; type CTRL-A to select the existing value and
then enter the new value so the old value is completely replaced.

Do not "Submit", just "Save" 

The task ends when call Capabilities have been entered.
    """
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
            }
        let plan =
            { OPlan.Default with
                description = "Take hours from jira and enter them into t-time"
//                root = ONode.All {nodes= [ONode.One tHours; ONode.One tCapability; ONode.One t_tTime]; description=None}
                //root = ONode.All {nodes= [ONode.One tCapability; ONode.One t_tTime]; description=None}
                root = ONode.All {nodes= [ONode.One t_tTime]; description=None}
            }
        plan

let s1 = Samples.sample()

//FsResponses.Log.debug_logging <- true
let kernel planRef =
    let b = Kernel.CreateBuilder()
    let mem = OPlanMemory()
    mem.save_memory("AGAP-8515", "22/Jun/25: 0; 23/Jun/25: 8; 24/Jun/25: 8; 25/Jun/25: 0; 26/Jun/25: 0; 27/Jun/25: 0; 28/Jun/25: 0") |> ignore
    mem.save_memory("AGAP-8515", "CAP-12033") |> ignore
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

