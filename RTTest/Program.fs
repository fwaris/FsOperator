module Pgm
open System
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.SemanticKernel
open FsOpCore

let runPlan = SpreadsheetToEmailPlan.create()
//let runPlan = TimesheetPlan.create()
//let runPlan = OPlan.sample()

//FsResponses.Log.debug_logging <- true

let initMem = 
    """
{
  "AGAP-8692": [
    "CAP-12033",
    "29Jun:0,30Jun:8,01Jul:8,02Jul:0,03Jul:0,04Jul:0,05Jul:0"
  ],
  "Timesheet Week": [
    "07-01-2025"
  ]
}
"""
    |> fun j -> JsonSerializer.Deserialize<Map<string,string list>>(j)

//let runPlan' = {runPlan with root = match runPlan.root with ONode.Seq all -> {all with nodes = all.nodes |> List.skip 2} |>  ONode.Seq | x -> x}
let s1rMem = ["Timesheet Week",["07-01-2025"]] |> Map.ofList
let s1r = OPlanRun.Create runPlan (OPlan.defaultKernel Map.empty  None)
//let s1r = OPlanRun.Create runPlan (TimesheetPlan.startKernel())

let s2r = OPlan.run s1r |> Async.RunSynchronously

let usages = s2r.completedTasks |> List.map _.usage |> OPlan.collectUsages |> OPlan.sumUsages
let costByModel = usages |> Map.toList |> List.map (fun (id,u) -> id,ModelPricing.calcPrice(id,u))
for m,c in costByModel do printfn $"Cost: {m}: $%0.2f{c}"
printfn $"Total cost: %0.2f{float(costByModel |> List.sumBy snd)}"

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1
