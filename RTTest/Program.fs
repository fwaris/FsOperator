module Pgm
open Microsoft.SemanticKernel
open FsOpCore
open System.Text.Json

module Samples =

    let sample() =
        let tHours =
            { OTask.Create() with
                id = "load_hours"
                description = "save task and daily hours from jira"
                target = OLink "https://t-mobile.atlassian.net/projects/AGAP?selectedItem=com.atlassian.plugins.atlassian-connect-plugin:is.origo.jira.tempo-plugin__tempo-project-centric-timesheet-panel"
                tools = FlUtils.makeFunctionTools<OPlanMemory>() @ FlUtils.makeFunctionTools<Navigator>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """Your goal is to record Jira task ids and associated daily hours
from Jira’s Timesheet view for the *current week*

Select the current week from the time selector.

Note down the Jira task id, date and hours and save them to memory. 
Example memory "JIRA_TASK-AGAP_XXX" => "22Jun:8,23Jun:0,24Jun:0,25Jun:8,26Jun:0,27Jun:0,28Jun:0"

Extract the information from the screenshots provided and invoke the save_memory function to save the data into memory.

**ALL information you need should all be available on a SINGLE PAGE. Don't click links to go to other pages.**
*If a horizontal or vertical scroll bar is visible for the Timesheet view*, scroll appropriately to see all data.

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.
To reset you may use the 'home' function to get back to the main page

Your task ends when all Jira task ids and their asscociated hours by day have been saved to memory
"""
                }

        let tCapability =
            { OTask.Create() with
                id = "get_capability_ids"
                description = "get capability ids for each task in jira"
                target = OLink "https://t-mobile.atlassian.net/projects/AGAP?selectedItem=com.atlassian.plugins.atlassian-connect-plugin:is.origo.jira.tempo-plugin__tempo-project-centric-timesheet-panel"
                tools = FlUtils.makeFunctionTools<OPlanMemory>() @ FlUtils.makeFunctionTools<Navigator>()
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
                cua = Some """Your goal is to save the Capability ID for each jira task

First, retrieve jira task ids from memory.

For each jira task id (that has hours) do the following:
1. Task Details: Use search box to locate the task details page
2. Parent Story: Breadcrumbs at top: [...] / [Story Link] / [Task Link]; use Story Link
3. Parent Feature: Breadcrumbs at top: [...] / [Feature Link] / [Story Link]; use Feature Link
4. Capability Id : On Feature Page Look for the Capability Id (starts with 'CAP')
5. Save capability id for each jira task id into memory, for example: "JIRA_TASK-AGAP-xxx"=>"CAP-xxxx". Ensure jira task id is associated with the Capability id
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
                tools = FlUtils.makeFunctionTools<OPlanMemory>() @ FlUtils.makeFunctionTools<Navigator>()
                description = "Enter capability hours into T-Time"
                cua = Some """Goal: Add a row for each Capability ID (from memory) for the selected week, entering weekday hours for related Jira tasks.

Instructions:

**Retrieve Capability and Jira task data from memory.**

On the page, select the date range matching the Jira tasks’ date range from memory.
Note: select any day of the week to see the whole week.

For each Capability ID (starts with "CAP"):

1. Enter the ID in the Capability field and click on the matching name from the filtered list to select the capability. Note, once selected, only the name shows; the capability id does not show.

2. Choose "NEW Functionality: Application Coding" for Activity Id.

3. Click Add to insert a new row.

4. Enter weekday hours. Select existing cell content (CTRL+A) and overwrite it. Note that its tricky to enter hours - just clicking in the box and entering say '8' hours will make it '80' hours as the existing '0' is not deleted.

Repeat Steps 1 - 4 for all Capability IDs.

Important:
Only enter data for Capability IDs found in memory.

Click Save, not Submit.

Finish once all Capability rows are entered.
    """
                reasoner = Some Prompts.``reasoner prompt for cua guidance``
            }
        let plan =
            { OPlan.Default with
                description = "Take hours from jira and enter them into t-time"
                //root = ONode.All {nodes= [ONode.One tHours; ONode.One tCapability; ONode.One t_tTime]; description=None}
                //root = ONode.All {nodes= [ONode.One tCapability; ONode.One t_tTime]; description=None}
                root = ONode.All {nodes= [ONode.One t_tTime]; description=None}
            }
        plan

let s1 = Samples.sample()

let memSnapshoot = """
{
  "AGAP-7495": [
    "CAP-12033"
  ],
  "AGAP-8279": [
    "CAP-12033"
  ],
  "AGAP-8515": [
    "CAP-12033"
  ],
  "JIRA_TASK-AGAP_8515": [
    "22Jun:0,23Jun:0,24Jun:8,25Jun:8,26Jun:0,27Jun:0,28Jun:0"
  ],
  "JIRA_TASK-AGAP_8586": [
    "22Jun:0,23Jun:8,24Jun:0,25Jun:0,26Jun:0,27Jun:0,28Jun:0"
  ]
}
"""

//FsResponses.Log.debug_logging <- true
let kernel nav =
    let b = Kernel.CreateBuilder()
    //b.Plugins.AddFromType<OPlanMemory>() |> ignore
    let mem = OPlanMemory()
    let memD = JsonSerializer.Deserialize<Map<string,string list>>(memSnapshoot)
    mem.SetMemory(memD)
    b.Plugins.AddFromObject(mem) |> ignore
    //b.Plugins.AddFromObject(OPlanMemory.LoadState()) |> ignore
    b.Plugins.AddFromObject(nav) |> ignore
    b.Build()

//holder for runtime plan (provides context for some function calls)
let nav = Navigator()

//kernel.Plugins.GetFunctionsMetadata() |> Seq.iter (fun x-> printfn "%s.%s" x.PluginName x.Name)
//kernel.Plugins.GetFunction("OPlanMemory", "save_memory")
let s1r = OPlanRun.Create s1 (kernel nav)

//let t1 = OPlan.run s1r |> Async.RunSynchronously
let s2r = OPlan.run nav.PlanRef s1r |> Async.RunSynchronously

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1

