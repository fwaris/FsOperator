module Pgm
open Microsoft.SemanticKernel
open FsOpCore

let s1 = OPlan.sample()

//FsResponses.Log.debug_logging <- true
let kernel = 
    let b = Kernel.CreateBuilder()
    b.Plugins.AddFromType<OPlanMemory>() |>  ignore
    b.Build()


//kernel.Plugins.GetFunctionsMetadata() |> Seq.iter (fun x-> printfn "%s.%s" x.PluginName x.Name)
//kernel.Plugins.GetFunction("OPlanMemory", "save_memory")
let s1r = OPlanRun.Create s1 kernel

let t1 = OPlan.step s1r |> Async.RunSynchronously

let i = 1