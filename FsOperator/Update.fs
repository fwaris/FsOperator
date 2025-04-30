namespace FsOperator
open System
open Elmish
open Avalonia.Media
open Avalonia
open Avalonia.Controls
open FSharp.Control
open Avalonia.FuncUI.Hosts

module Update =
    
    let init _   = Model.Default,Cmd.ofMsg Initialize

    let update (win:HostWindow) msg (model:Model) =
        try
            match msg with
            | Initialize -> model, Cmd.none
            | _ -> model, Cmd.none
        with ex -> 
            printfn "%A" ex
            model, Cmd.none