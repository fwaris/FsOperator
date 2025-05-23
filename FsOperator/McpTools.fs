﻿module McpTools
open FsOpCore
open FsOperator
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open ModelContextProtocol.Server
open System
open System.ComponentModel
open System.Globalization
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open ModelContextProtocol
open ModelContextProtocol.Server


[<AutoOpen>]
module HttpClientExt =

    type HttpClient with
        member this.ReadJsonDocumentAsync(requestUri: string) : Task<JsonDocument> =
            task {
                use! response = this.GetAsync(requestUri)
                response.EnsureSuccessStatusCode() |> ignore
                use! stream = response.Content.ReadAsStreamAsync()
                return! JsonDocument.ParseAsync(stream)
            }

[<McpServerToolType>]
type JiraTools() =

    [<McpServerTool>]
    [<Description("Add Jira Task")>]
    static member AddJiraTask
        (
            client: HttpClient,
            [<Description("Jira Task Name")>] taskName: string,
            [<Description("Jira Task Description")>] description: string
        ) : Task<string> =
        task {
            let jiraTask  = 
                {
                    id="mcp jira task"
                    description="Add task to jira"
                    url="https://jirasw.t-mobile.com/secure/RapidBoard.jspa?rapidView=23224&quickFilter=101511#" 
                    voiceAsstInstructions= ""
                    textModeInstructions = $"""Instruction: Create a sub-task under the story AGAP-7498 using only the designated method. Do not make any other changes.

Constraints:
Only use the "Create sub-task" action available on the parent story's page.

Do not modify any other data or perform unrelated actions.

Steps to Follow:
1. Search for and open the parent story AGAP-7498.
Use the search box to locate and navigate to the main page of this story.

2. Access the available actions menu.
** On the parent story page, click on the **"More"** menu to open the list of actions. **
** On the parent story page, click on the **"More"** menu to open the list of actions. **

3. Scroll to find the correct action.
In the "More" dropdown, scroll to the bottom and select the "Create sub-task" action.

Fill in the following details when prompted:

|Field|Value|
|-----|------|
|Summary | {taskName} |
| Description | {description} |

"""
                }

            Update.mailbox.Writer.TryWrite (ClientMsg.Log_Append $"<-- MCP Jira task: {taskName} - {description}") |> ignore
            Update.mailbox.Writer.TryWrite (ClientMsg.OpTask_Loaded (Some jiraTask))  |> ignore
            return "Done"
        }


