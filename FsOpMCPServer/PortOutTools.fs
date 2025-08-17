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
type PortOutTools() =

    [<McpServerTool>]
    [<Description("Receive bill")>]
    static member ReceiveBill
        (          
            [<Description("File name")>] fileName: string,
            [<Description("File bytes")>] fileBytes: byte[]
        ) : Task<string> =
        task {
            printfn $"recieved {fileName}"
            let bill = {FileName=fileName; Data=fileBytes}
            Subscriptions.mailbox.Writer.TryWrite(GotBill bill) |> ignore            
            return "Done"
        }
