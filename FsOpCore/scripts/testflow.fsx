#load "packages.fsx"
open FsResponses
open FsOpCore
open Microsoft.SemanticKernel

1855.00 - 285.00

let testWorkFlow() = 
    let post x = printfn "%A" x
    let ui = PlaywrightDriver.create()
    let ch = Chat.Default

    let fl = TaskFlow.create post ui.driver ch
    fl.Post TaskFlow.TFi_Start
    fl.Terminate()

let fns = OPlanMemory.functions() |> Seq.toList
let f0 = fns.[0]
f0.Parameters.[0]
f0.AdditionalProperties
f0.Description
f0.Name
f0.Parameters 

let toFunction (metadata:KernelFunctionMetadata) =     
    {Function.Default with 
        name = metadata.Name
        description = metadata.Description
        parameters = 
            {Parameters.Default with 
                properties = 
                    metadata.Parameters
                    |> Seq.map (fun (mp:KernelParameterMetadata)  -> 
                        mp.Name,
                        {
                            Property.``type`` = mp.ParameterType.Name.ToLower()
                            Property.description = mp.Description |> checkEmpty
                        }
                    )
                    |> Map.ofSeq           
                required = 
                    metadata.Parameters 
                    |> Seq.choose (fun p -> if p.IsRequired then Some p.Name else None)
                    |> Seq.toList
            }        
    }

let kernelArgsDefault (args:(string*obj) seq) =
    let sttngs = PromptExecutionSettings()
    let kargs = KernelArguments(sttngs)
    for (k,v) in args do
        kargs.Add(k,v)
    kargs


let sch = toFunction fns.[0]

let b = Kernel.CreateBuilder()
b.Plugins.AddFromType<OPlanMemory>()
let k = b.Build()
let save_memory = k.Plugins.GetFunction(f0.PluginName,f0.Name)
let get_memory = k.Plugins.GetFunction(f0.PluginName,"get_memory")

let ks  : KernelArguments = kernelArgsDefault ["key","a"; "value", "b"]
let r = save_memory.InvokeAsync(k,arguments=ks).Result

open System.Collections.Generic
open System.Text.Json
let save_memory_arg = """{"key":"a", "value":"b"}""" |> JsonSerializer.Deserialize<IDictionary<string,obj>>
let get_memory_arg =  """{"key":"a"}"""  |> JsonSerializer.Deserialize<IDictionary<string,obj>>
let k2 = Kernel.CreateBuilder().Build()
let plugin = OPlanMemory()
k2.ImportPluginFromObject(plugin)
k2.InvokeAsync(save_memory,KernelArguments( save_memory_arg)).Result.GetValue<string>()
let rslt = k2.InvokeAsync(get_memory,KernelArguments(get_memory_arg)).Result
let rslts = rslt.GetValue() |> JsonSerializer.Serialize
let rsltso : obj = rslt.GetValue()
JsonSerializer.Serialize(rsltso,options=FsResponses.Api.serOpts)



    