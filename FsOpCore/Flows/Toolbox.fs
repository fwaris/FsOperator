namespace FsOpCore
open System.Reflection
open Microsoft.FSharp.Reflection
open System
open System.Reflection
open Microsoft.SemanticKernel
open FsResponses
open System.Text.Json

module Toolbox = 

    /// <summary>
    /// Convert metadata to 'function' tool for use with <see cref="FsResponses.Request" />.
    /// Also see <see cref="FlUtils.functionMetadata" />.
    /// </summary>
    let toFunctionTool (metadata:KernelFunctionMetadata) =
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
                                Property.description = mp.Description |> checkEmpty |> Option.defaultValue ""
                            }
                        )
                        |> Map.ofSeq
                    required =
                        metadata.Parameters
                        |> Seq.choose (fun p -> if p.IsRequired then Some p.Name else None)
                        |> Seq.toList
                }
        }
        //|> Tool_Function

    ///<summary>
    ///Extract function metadata from a properly annotated type.<br />
    ///Members tagged with KernelFunction("...") attributes are included.<br />
    ///Use <see cref="FlUtils.toFunction"/> to convert metadata to 'function' tool.
    ///</summary>
    let functionMetadata<'t> () =
        let b = Kernel.CreateBuilder()
        b.Plugins.AddFromType<'t>() |> ignore
        let k = b.Build()
        let fs = k.Plugins.GetFunctionsMetadata()
        fs

    ///<summary>
    ///Make a list of 'function' tools that can be used with a <see cref="FsResponses.Request" /><br />
    ///The functions are extracted from a properly annotated type.<br />
    ///See <see cref="FlUtils.functionMetadata"/>.
    ///</summary>
    let makeFunctionTools<'t>() = functionMetadata<'t>() |> Seq.map toFunctionTool |> Seq.toList

    ///call an individual function
    let invokeFunction (kernel:Kernel) (name:string) (arguments:string) = async {
        try
            let args = JsonSerializer.Deserialize<Map<string,obj>>(arguments)
            let args = args |> Map.toSeq |> Prompts.kernelArgs
            let! rslt = kernel.InvokeAsync(pluginName=null,functionName=name,arguments=args) |> Async.AwaitTask
            let str = rslt.GetValue()
            let rsltStr = JsonSerializer.Serialize(str)
            return rsltStr
        with ex -> 
            Log.exn (ex,"invokeFunction")
            return "unable to invoke function"
    }

    let internal toolboxType = lazy (
        let assembly = Assembly.GetExecutingAssembly()
        // Find all F# modules in the assembly:
        let moduleTypes = assembly.GetTypes() |> Array.filter FSharpType.IsModule
        // e.g., pick a specific module by name:
        moduleTypes |> Array.find (fun t -> t.Name = "Toolbox"))


    let internal fmMethod = lazy(toolboxType.Value.GetMethod("makeFunctionTools"))
    
    let internal get (t:Type) = 
        fmMethod.Value.MakeGenericMethod([|t|]).Invoke(null,[||])
        :?> Function list

    let tools (ts:Type list) = ts |> List.collect get

