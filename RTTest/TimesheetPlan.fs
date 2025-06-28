module TimesheetPlan
open FsOpCore
open Microsoft.SemanticKernel
open System.Text.Json

let tHours =
    { OTask.Create() with
        id = "load_hours"
        description = "save task and daily hours from jira"
        target = OLink "https://t-mobile.atlassian.net/projects/AGAP?selectedItem=com.atlassian.plugins.atlassian-connect-plugin:is.origo.jira.tempo-plugin__tempo-project-centric-timesheet-panel"
        tools = FlUtils.makeFunctionTools<OPlanMemory>() @ FlUtils.makeFunctionTools<Navigator>()
        reasoner = Some Prompts.``reasoner prompt for cua guidance``
        cua = Some """Your goal is to record Jira task ids and associated daily hours
from Jira’s Timesheet view for the "Timesheet Week".

# How to get the "Timesheet Week"
-- *USE get_memory function to get the "Timesheet Week" from memory data*
-- If not found, use the week in which today's date falls

For chosen week, select the 'Days' view that in the Timesheet view so the daily hours are visible for the whole week.

Note down the Jira task id, date and hours and save them to memory.
Example memory "JIRA_TASK-AGAP_XXX" => "22Jun:8,23Jun:0,24Jun:0,25Jun:8,26Jun:0,27Jun:0,28Jun:0"

*Ensure that for each day, no more that 8 hours are recorded across all tasks.* Ignore any aggregagted hours. Pay attention to DAY values only

Extract the information from the screenshots provided and invoke the save_memory function to save the data into memory.

**ALL information you need should all be available on a SINGLE PAGE. Don't click links to go to other pages.**
*If a horizontal or vertical scroll bar is visible for the Timesheet view*, scroll appropriately to see all data.

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.
To reset you may use the 'home' function to get back to the main page

Your task ends when all Jira task ids and their associated hours by day have been saved to memory

Important: Make sure the hours captured are for the "Timesheet Week".
"""}

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
2. Parent Story: Breadcrumbs at top: Project / [...] / [Story Link] / [Task Link]; use Story Link
3. Parent Feature: Breadcrumbs at top: Project / [...] / [Feature Link] / [Story Link]; use Feature Link
4. Capability Id : On Feature Page Look for the Capability Id (starts with 'CAP')
5. Save capability id for each jira task id into memory, for example: "JIRA_TASK-AGAP-xxx"=>"CAP-xxxx". Ensure jira task id is associated with the Capability id
6. Use 'home' function to get back to the home page
7. Repeat 1. to 6. for the next jira task with hours, if any

**End the CUA task when the Capability IDs for all tasks have been saved.**

Note: If needed, use Faisal.Waris1@t-mobile.com as login email id.
"""}


let t_tTime =
        { OTask.Create() with
            id = "enter_hours_into_t-time"
            target = OLink "https://apps.powerapps.com/play/e/7ccae0f5-3b24-4e97-a2a1-0171636f64ff/a/e9ecf476-d164-41f6-b24b-84d14f4a3b6f"
            tools = FlUtils.makeFunctionTools<OPlanMemory>() @ FlUtils.makeFunctionTools<Navigator>()
            description = "Enter capability hours into T-Time"
            reasoner = Some Prompts.``reasoner prompt for cua guidance``
            cua = Some """Goal: Add a row for each Capability ID (from memory) for the selected week, entering weekday hours for related Jira tasks.
Instructions:

# First, use get_memory function to Retrieve Capability and Jira task data from memory

Also note the 'Timesheet Week' retrieved from memory.

On the page, select the date range matching the Jira tasks’ date range from memory.
Note: select any day of the week to see the whole week.

For each Capability ID (starts with "CAP"):

1. Ensure row for Capability and Activity:
Click 'Capability' dropdown and *type* the Capability ID in the search box. Then **click** on the name in the filtered list to select the capability. No need to scroll. **Note, once selected, only the name shows; the capability id does not show. Make sure the capability name is showing otherwise repeat this step**

2. Choose "NEW Functionality: Application Coding" for Activity Id.

3. Click Add to insert a new row.

4. Enter weekday hours for each day. Sum all hours for each day for the tasks under the same Capability ID. **After clicking into the day cell, use CTRL-A to select exsting hours and then enter the new hours**. Make sure the hours are correct, e.g. '8' hours shoud not be '80' hours.

Repeat Steps 1 - 4 for all Capability IDs.

Finally, ensure all hours look right by comparing to data in memory. Fix the incorrect hours, if required.

Important:
Only enter data for Capability IDs found in memory.

Click Save, not Submit.

*The task ends after the timesheet is Saved.*
    """}

let create() =
        let plan =
            { OPlan.Default with
                description = "Take hours from jira and enter them into t-time"
                root = ONode.All {nodes= [ONode.One tHours; ONode.One tCapability; ONode.One t_tTime]; description=None}
                //root = ONode.All {nodes= [ONode.One tCapability; ONode.One t_tTime]; description=None}
                //root = ONode.All {nodes= [ONode.One t_tTime]; description=None}
            }
        plan

let startMemory =
  [
    "Timesheet Week", ["6/2/2025"]
  ]
  |> Map.ofList

let startCapMemory = 
    let m = """
{
  "JIRA_TASK-AGAP-8515": [
    "CAP-12033",
    "22Jun:0,23Jun:8,24Jun:8,25Jun:0,26Jun:0,27Jun:0,28Jun:0"
  ],
  "JIRA_TASK-AGAP-8586": [
    "CAP-12033",
    "22Jun:0,23Jun:0,24Jun:0,25Jun:8,26Jun:8,27Jun:0,28Jun:0"
  ],
  "JIRA_TASK-AGAP-8692": [
    "CAP-12033",
    "22Jun:0,23Jun:0,24Jun:0,25Jun:0,26Jun:0,27Jun:8,28Jun:0"
  ],
  "Timesheet Week": [
    "6/9/2025"
  ]
}
"""
    JsonSerializer.Deserialize<Map<string,string list>>(m)

let startKernel nav =
    let b = Kernel.CreateBuilder()
    let mem = OPlanMemory()
    mem.SetMemory(startMemory)
    //mem.SetMemory(startCapMemory)
    b.Plugins.AddFromObject(mem) |> ignore
    b.Plugins.AddFromObject(nav) |> ignore
    b.Build()

///memory to test only the last task
let memSnapshoot_t_tTime = """
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

let testKernel nav =
    let b = Kernel.CreateBuilder()
    //b.Plugins.AddFromType<OPlanMemory>() |> ignore
    let mem = OPlanMemory()
    let memD = JsonSerializer.Deserialize<Map<string,string list>>(memSnapshoot_t_tTime)
    mem.SetMemory(memD)
    b.Plugins.AddFromObject(mem) |> ignore
    //b.Plugins.AddFromObject(OPlanMemory.LoadState()) |> ignore
    b.Plugins.AddFromObject(nav) |> ignore
    b.Build()
