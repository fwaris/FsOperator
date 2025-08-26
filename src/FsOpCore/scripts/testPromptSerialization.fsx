#load "packages.fsx"
open System.Text.Json
open System.Text.Json.Serialization
open System
open System.ComponentModel
open FsResponses
open FsOpCore
open Microsoft.SemanticKernel

let steps =
    [
        {
            step_num = 1
            step_required = Requirement.Required
            step_instructions = "first instruction"
            step_status = Status.Done
        }
        {
            step_num = 2
            step_required = Requirement.Required
            step_instructions = "second instruction"
            step_status = Status.ToDo
        }
        {
            step_num = 3
            step_required = Requirement.Optional
            step_instructions = "third instruction"
            step_status = Status.ToDo
        }
    ]

let promptTemplate = $"""
{{{{${Vars.steps}}}}}
"""
(*
let str1 = Prompts.toJson steps
let str =  [Vars.steps, Prompts.toJson steps :> obj] |> Prompts.renderPrompt promptTemplate
*)


