module Pgm
open System
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.SemanticKernel
open FsOpCore

//Sandbox.test()

let runPlan,kernel = PortingPlan2.createWithKernel()
let s1r = OPlanRun.Create runPlan kernel

let s2r = OPlan.run s1r |> Async.RunSynchronously

let usages = s2r.completedTasks |> List.map _.usage |> OPlan.collectUsages |> OPlan.sumUsages
let costByModel = usages |> Map.toList |> List.map (fun (id,u) -> id,ModelPricing.calcPrice(id,u))
for m,c in costByModel do printfn $"Cost: {m}: $%0.2f{c}"
printfn $"Total cost: %0.2f{float(costByModel |> List.sumBy snd)}"
printfn $"Total duration: {s2r.Duration.TotalMinutes} minutes"

for t in s1r.completedTasks do
    for m in t.messages do
        printfn "%A" m

let i = 1
