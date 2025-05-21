#r "nuget: ModelContextProtocol, 0.1.0-preview.8"

open System
open System.Collections.Generic
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol.Transport

let mcpAddTask () = task {
    //transport and client
    let sseOptions = SseClientTransportOptions(Endpoint = Uri("http://localhost:5000/sse"),Name = "FSharpClient")
    use httpClient = new HttpClient()
    let transport = SseClientTransport(sseOptions, httpClient, ownsHttpClient = false)
    let! client = McpClientFactory.CreateAsync(transport)

    //tool call
    let parms = readOnlyDict [
        "taskName",    "CUA Demo Prep"  :> obj
        "description", "Finish MCP integration"
    ]    
    let! result = client.CallToolAsync("AddJiraTask", parms)

    // Extract and print the text content from the result
    let textContent = result.Content |> Seq.tryFind (fun c -> c.Type = "text")
    match textContent with
    | Some content -> printfn "Tool response: %s" content.Text
    | None -> printfn "No text content received."
}

mcpAddTask().Result

(*
    let! tools = client.ListToolsAsync()
    printfn "Available tools:"
    for tool in tools do
        printfn " - %s: %s" tool.Name tool.Description
        printfn "%A" tool.JsonSchema

*)