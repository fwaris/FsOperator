module Pgm
open Microsoft.SemanticKernel
open System.Text.Json
open FsOpCore

let runPlan = SpreadsheetToEmailPlan.create()

//FsResponses.Log.debug_logging <- true
let defaultKernel nav =
    let b = Kernel.CreateBuilder()
    let mem = OPlanMemory()
    b.Plugins.AddFromObject(mem) |> ignore
    b.Plugins.AddFromObject(nav) |> ignore
    b.Build()

//holder for runtime plan (provides context for some function calls)
let nav = Navigator()

let s1r = OPlanRun.Create runPlan (SpreadsheetToEmailPlan.testKernel nav)

let s2r = OPlan.run nav.PlanRef s1r |> Async.RunSynchronously

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1
