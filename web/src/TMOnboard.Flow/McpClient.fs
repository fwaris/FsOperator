module McpClient
open System
open System.IO
open FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.Text.Json

module MCP = 
    open ModelContextProtocol.Client
    open System.Net.Http
    let sendBill (fileName:string, fileBytes:byte[]) = task {
        //transport and client
        let sseOptions = SseClientTransportOptions(Endpoint = Uri("http://localhost:5000/sse"),Name = "FSharpClient")
        use httpClient = new HttpClient()
        let transport = SseClientTransport(sseOptions, httpClient, ownsHttpClient = false)
        let! client = McpClientFactory.CreateAsync(transport)

        //tool call
        let parms = readOnlyDict [
            "fileName", fileName :> obj
            "fileBytes",   fileBytes
        ]    
        let! result = client.CallToolAsync("ReceiveBill", parms)

        // Extract and print the text content from the result
        let textContent = result.Content |> Seq.tryFind (fun c -> c.Type = "text")
        match textContent with
        | Some content -> printfn "Tool response: %s" content.Text
        | None -> printfn "No text content received."
    }

