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
type WeatherTools() =

    [<McpServerTool>]
    [<Description("Get weather alerts for a US state.")>]
    static member GetAlerts
        (
            client: HttpClient,
            [<Description("The US state to get alerts for. Use the 2 letter abbreviation for the state (e.g. NY).")>] state: string
        ) : Task<string> =
        task {
            use! jsonDocument = client.ReadJsonDocumentAsync($"/alerts/active/area/{state}")
            let alerts = jsonDocument.RootElement.GetProperty("features").EnumerateArray()

            if not (alerts |> Seq.exists (fun _ -> true)) then
                return "No active alerts for this state."
            else
                let results =
                    alerts
                    |> Seq.map (fun alert ->
                        let props = alert.GetProperty("properties")
                        $"""
                        Event: {props.GetProperty("event").GetString()}
                        Area: {props.GetProperty("areaDesc").GetString()}
                        Severity: {props.GetProperty("severity").GetString()}
                        Description: {props.GetProperty("description").GetString()}
                        Instruction: {props.GetProperty("instruction").GetString()}
                        """)
                    |> String.concat "\n--\n"

                return results
        }

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

