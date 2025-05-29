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
            [<Description("Parent Task Id")>] parentTaskId: string,
            [<Description("Jira Task Name")>] taskName: string,
            [<Description("Jira Task Description")>] description: string
        ) : Task<string> =
        task {
            let jiraTask  = 
                {
                    id="mcp jira task"
                    description="Add task to jira"
                    url = $"https://jirasw.t-mobile.com/browse/{parentTaskId.Trim()}" 
                    voiceAsstInstructions= ""
                    textModeInstructions = $"""Goal: Create a sub-task under the story {parentTaskId}.

**JUST CREATE SUB-TASK**

2. Scroll page down 2 times - use PAGEDOWN/ PAGEUP to scroll

3. Click '+' next to sub-tasks

4. In 'Summary'  put "{taskName}"

5. In 'Description' put "{description}"

"""
                }

            Update.mailbox.Writer.TryWrite (ClientMsg.Log_Append $"<-- MCP Jira task: {taskName} - {description}") |> ignore
            Update.mailbox.Writer.TryWrite (ClientMsg.OpTask_Loaded (Some jiraTask))  |> ignore
            return "Done"
        }
