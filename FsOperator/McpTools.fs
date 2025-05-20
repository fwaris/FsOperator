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
            [<Description("Jira Task Description")>] description: double
        ) : Task<string> =
        task {
            let jiraTask  = 
                {
                    id="mcp jira task"
                    description="Add task to jira"
                    url="https://jirasw.t-mobile.com/secure/RapidBoard.jspa?rapidView=23224&quickFilter=101511#" 
                    voiceAsstInstructions= ""
                    textModeInstructions = $"""
Create or update a Jira task with the name $'{taskName}'
and description $'{description}'.
Create the task under the story 'Research into various Agentic offerings'
"""
                }

            Update.mailbox.Writer.TryWrite (ClientMsg.Log_Append $"Jira task: {taskName} - {description}") |> ignore
            return "Done"
        }


