module McpTools
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
                    textModeInstructions = $"""Your goal is to create OR update a jira sub-task.

Sub Task info:
- name: '{taskName}'
- description '{description}'.

Here are the steps to follow:
1. Locaate parent story 'AGAP-7498'
2. See if the sub-task already exits under this story
3. If story exits, update the description otherwise create a new sub-task
4.  If the sub-task is assigned, assign it to 'Faisal Waris'

Helpful Notes and Hints:
-  Use the JQL query : parent = AGAP-xxxx to find the existing sub-tasks
- Use search box to find jira 'issues' (stories, sub-tasks, etc).
- The sub-task can only be added from the story detail page
- You can get to the detail page of an issue by clicking on the 'AGAP' link.
- ** Use the "More" menu on the AGAP-7498 story to create a new sub-task; Scroll down on the menu find the create option**

"""
                }

            Update.mailbox.Writer.TryWrite (ClientMsg.Log_Append $"<-- MCP Jira task: {taskName} - {description}") |> ignore
            Update.mailbox.Writer.TryWrite (ClientMsg.OpTask_Loaded (Some jiraTask))  |> ignore
            return "Done"
        }


