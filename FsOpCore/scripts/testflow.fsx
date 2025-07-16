#load "packages.fsx"
open System.Text.Json
open System.Text.Json.Serialization
open System
open System.ComponentModel
open FsResponses
open FsOpCore
open Microsoft.SemanticKernel
open FsResponses

let p1 = OPlan.sample()
let ts = p1.root.allTasks().Head
ts.cua

let prompt1 = Prompts.renderPrompt Prompts.``divide cua instructions into granular chunks`` ( Prompts.kernelArgs [Vars.cuaInstructions, ts.cua])

type Status =  ToDo = 0 | Done = 1
type CuaInstructionStep =
    {
        step_num: int
        step_instructions: string

        //[<Description("0=ToDo, 1=Done")>]
        step_status : Status
    }

type CuaInstructions = {
    steps: CuaInstructionStep list
}

type CuaInstructionsResponse = {
    task_complete : bool
    cua_guidance : string
}

match RUtils.structuredFormat (typeof<CuaInstructions>) with {format=Json_schema f} -> f.schema

let req =
    {Request.Default with
        input = [IOitem.Message {Message.Default with content = [Content.Input_text {|text = prompt1|}]}]
        text = RUtils.structuredFormat (typeof<CuaInstructions>) |> Some
    }

let resp = (Api.create req (Api.defaultClient())).Result
let outT = RUtils.outputText resp
sprintf "%s" outT

let serOpts =
    let o = JsonSerializerOptions(JsonSerializerDefaults.General)
    o.Converters.Add(JsonStringEnumConverter())
    o.WriteIndented <- true
    o.ReadCommentHandling <- JsonCommentHandling.Skip
    let opts = JsonFSharpOptions.Default()
    opts
        .WithSkippableOptionFields(true)
        .AddToJsonSerializerOptions(o)
    o

let steps = System.Text.Json.JsonSerializer.Deserialize<Reasoner.CuaInstructions>(outT,serOpts)




