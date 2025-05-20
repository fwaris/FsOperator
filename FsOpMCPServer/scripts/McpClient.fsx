#r "nuget: ModelContextProtocol, 0.1.0-preview.8"

open System
open System.Collections.Generic
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol.Transport

let useMcpClient () = task {
    // Configure the SSE transport to connect to the MCP server
    let sseOptions = SseClientTransportOptions(
        Endpoint = Uri("http://localhost:5000/sse"),
        Name = "FSharpClient"
    )

    // Create an HTTP client for the SSE transport
    use httpClient = new HttpClient()

    // Initialize the SSE client transport
    let transport = SseClientTransport(sseOptions, httpClient, ownsHttpClient = false)

    // Create the MCP client
    let! client = McpClientFactory.CreateAsync(transport)

    // List available tools
    let! tools = client.ListToolsAsync()
    printfn "Available tools:"
    for tool in tools do
        printfn " - %s: %s" tool.Name tool.Description
        printfn "%A" tool.JsonSchema

    // Prepare parameters for the 'GetCurrentTime' tool
    let parameters = Dictionary<string, obj>()
    parameters.Add("taskName", box "CUA Demo Prep")
    parameters.Add("description", box "Finish MCP integration")

    // Call the 'GetCurrentTime' tool
    let! result = client.CallToolAsync("AddJiraTask", parameters, cancellationToken = CancellationToken.None)

    // Extract and print the text content from the result
    let textContent = result.Content |> Seq.tryFind (fun c -> c.Type = "text")
    match textContent with
    | Some content -> printfn "Tool response: %s" content.Text
    | None -> printfn "No text content received."
}

// Run the MCP client function
useMcpClient()
|> Async.AwaitTask
|> Async.RunSynchronously
