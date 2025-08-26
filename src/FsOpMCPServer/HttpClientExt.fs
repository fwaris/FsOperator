namespace FsOpMCPServer


open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

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
