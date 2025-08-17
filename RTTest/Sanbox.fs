module Sandbox
open FsOpCore
open PortingPlan
open System.Text.Json
let tools = Toolbox.tools [
                    // typeof<Functions.FsOpMemory>
                    // typeof<Functions.FsOpNavigator>
                    // typeof<Functions.FsOpTaskTools>
                    typeof<BillFunctions>
                ]

let test() = 
    let req = 
        {FsResponses.Request.Default with 
            tools = tools |> List.map FsResponses.Tool.Function
        }

    let reqstr = JsonSerializer.Serialize(req,options=FsResponses.Api.serOpts)
    printfn "%s" reqstr
