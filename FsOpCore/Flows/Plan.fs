namespace FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open FsResponses

type OPlanMemory() =
    let mutable map = Map.empty

    [<KernelFunction("save_memory")>]
    [<Description("Save a key-value pair for later retrieval")>]
    member this.save_memory(key:string, value:string) = map <- map |> Map.add key value

    [<KernelFunction("get_all_keys")>]
    [<Description("retrieve all keys in the memory store ")>]
    member this.get_all_keys() = map.Keys |> Seq.toList

    [<KernelFunction("get_memory")>]
    [<Description("retrieve a value for the given key")>]
    member this.get_memory(key:string) = map |> Map.tryFind key

    static member functions() = 
        let b = Kernel.CreateBuilder()
        b.Plugins.AddFromType<OPlanMemory>() |> ignore
        let k = b.Build()
        let fs = k.Plugins.GetFunctionsMetadata()
        fs
        
type OTaskTarget = OProcess of string*string option | OLink of string

type OTask = {
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Tool list
}
    with 
        static member Default =  {
                target = OLink ""
                description = ""
                cua = None
                reasoner = None
                voice = None
                tools = []
            }
      

type OTaskTransition = {
    transition : string option
    task       : OTask
}

type OPlan = {
    description : string
    tasks  : OTaskTransition list
}
    with 
        static member Default = {
                        description = ""
                        tasks = []
                    }

type OTaskRun = {
    task     : OTask
    driver   : IUIDriver
    messages : ChatMsg list    
}

module OPlan =
    let functionMetadata<'t> () =
        let b = Kernel.CreateBuilder()
        b.Plugins.AddFromType<'t>() |> ignore
        let k = b.Build()
        let fs = k.Plugins.GetFunctionsMetadata()
        fs

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
        |> Tool_Function

    let makeFunctionTools<'t>() = functionMetadata<'t>() |> Seq.map toFunction |> Seq.toList

    let sample = 
        let ln = 
            { OTask.Default with
                target = OLink "https://www.linkedin.com"
                description = "find people who post about generative ai"
                tools = makeFunctionTools<OPlanMemory>()
                cua = Some """find individuals who have original posts
related to generative AI and record there linkedin names and profile links.
Use the save_memory function to record each name as you find it.
"""
                }
        let tw = 
            { OTask.Default with
                target = OLink "https://www.twitter.com"
                tools = makeFunctionTools<OPlanMemory>()
                description = "retrieve linkedIn people info from memory and get twitter handles"
                cua = Some """
list of names and linked in profile links. Search each name on twitter and obtain their
twitter handle. 
Use save_memory function to save each person's linked-in and twitter data
    """        
            }
        let plan = 
            { OPlan.Default with
                description = "take linkedin people and find their twitter handle"
            }
        plan
   
    