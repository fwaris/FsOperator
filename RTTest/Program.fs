module Pgm
open System
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.SemanticKernel
open FsOpCore

//let runPlan = SpreadsheetToEmailPlan.create()
let runPlan = TimesheetPlan.create()
//let runPlan = OPlan.sample()

//FsResponses.Log.debug_logging <- true

let initMem = 
    """
{
  "AGAP-7856": [
    "CAP-12033",
    "01Jun25:8,02Jun25:0,03Jun25:0,04Jun25:0,05Jun25:0,06Jun25:0,07Jun25:0"
  ],
  "AGAP-8082": [
    "CAP-12033",
    "01Jun25:0,02Jun25:8,03Jun25:0,04Jun25:0,05Jun25:0,06Jun25:0,07Jun25:0"
  ],
  "AGAP-8090": [
    "CAP-12033",
    "01Jun25:0,02Jun25:0,03Jun25:8,04Jun25:8,05Jun25:0,06Jun25:0,07Jun25:0"
  ],
  "Timeshee Week": [
    "06-02-2025"
  ],
  "Timesheet Week": [
    "01/Jun/25 - 07/Jun/25"
  ]
}
    """
    |> fun j -> JsonSerializer.Deserialize<Map<string,string list>>(j)

let runPlan' = {runPlan with root = match runPlan.root with ONode.All all -> {all with nodes = all.nodes |> List.skip 2} |>  ONode.All | x -> x}
let s1rMem = ["Timeshee Week",["06-02-2025"]] |> Map.ofList
let s1r = OPlanRun.Create runPlan' (OPlan.defaultKernel initMem None)
//let s1r = OPlanRun.Create runPlan (TimesheetPlan.startKernel())

let s2r = OPlan.run s1r |> Async.RunSynchronously

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1
