#load "packages.fsx"
#load @"../../RTTest/PortingPlan.fs"
open FsOpCore
open System.Text.Json
open FsResponses.Api
open PortingPlan

let tools = Toolbox.tools [
                    // typeof<Functions.FsOpMemory>
                    // typeof<Functions.FsOpNavigator>
                    // typeof<Functions.FsOpTaskTools>
                    typeof<BillFunctions>
                ]
let req = 
    {FsResponses.Request.Default with 
        tools = tools |> List.map FsResponses.Tool.Function
    }

let reqstr = JsonSerializer.Serialize(req,options=serOpts)
printfn "%s" reqstr

