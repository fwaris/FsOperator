module Pgm
open System
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.SemanticKernel
open FsOpCore

//let runPlan = SpreadsheetToEmailPlan.create()
//let runPlan = TimesheetPlan.create()
let runPlan = OPlan.sample()

//FsResponses.Log.debug_logging <- true

let initMem = 
    """
{
  "Sanders Riddle": [
    "https://linkedin.com/in/sandersriddle"
  ],
  "Shawn Olds": [
    "https://linkedin.com/in/shawnnolds"
  ],
  "Todd Paris": [
    "https://linkedin.com/in/toddparis"
  ]
}
    """
    |> fun j -> JsonSerializer.Deserialize<Map<string,string list>>(j)

let runPlan' = {runPlan with root = match runPlan.root with ONode.All all -> {all with nodes = all.nodes |> List.skip 1} |>  ONode.All | x -> x}

//let s1r = OPlanRun.Create runPlan (SpreadsheetToEmailPlan.testKernel nav)
let s1r = OPlanRun.Create runPlan' (OPlan.defaultKernel initMem None)
//let s1r = OPlanRun.Create runPlan (TimesheetPlan.startKernel())

let s2r = OPlan.run s1r |> Async.RunSynchronously

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1
