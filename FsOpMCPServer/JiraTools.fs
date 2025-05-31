namespace FsOpMCPServer

open System
open System.ComponentModel
open System.Globalization
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open ModelContextProtocol
open ModelContextProtocol.Server

[<McpServerToolType>]
type JiraTools() =

    [<McpServerTool>]
    [<Description("Add Jira Task")>]
    static member AddJiraTask
        (
            //client: HttpClient,
            [<Description("Jira Task Name")>] taskName: string,
            [<Description("Jira Task Description")>] description: string 
        ) : Task<string> =
        task {
            printfn $"name {taskName}"
            printfn $"desc {description}"
            return "Done"
        }

