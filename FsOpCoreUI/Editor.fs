namespace FsOpTaskEditor
open Elmish
open FsOpCore
open Avalonia.Input
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks
open Elmish
open FsOpCore
open Avalonia.FuncUI.Hosts
open Avalonia.Media
open Avalonia.Controls
open Avalonia.Input
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Elmish
open Avalonia.Layout
open Avalonia.Threading
open Avalonia.FuncUI.Types
open Avalonia.Controls.Templates

type Model = {
    refNode : ONode
    task : OTask   
}

type Msg =
    | Close
    | Save

module Update =
    let init (n:ONode) = 
        let t = match n with ONode.Leaf t -> t | _ ->  failwith "leaf node expected"
        {
            refNode = n
            task = t
        }
        ,Cmd.none

    let update (win:HostWindow) (tcs:TaskCompletionSource<(ONode*ONode)option>) msg (model:Model) = 
        match msg with
        | Close -> tcs.SetResult(None); win.Close(); model,Cmd.none
        | Save -> tcs.SetResult(Some(model.refNode,ONode.Leaf model.task));win.Close(); model,Cmd.none

module Views =
    let main model dispatch = TextBlock.create [TextBlock.text "edit"] :> IView
        
type TaskEditor(n:ONode) as this =
    inherit HostWindow()
    let tcs = new System.Threading.Tasks.TaskCompletionSource<(ONode*ONode) option>()

    do
        base.Title <- "Task Editor"
        base.Width <- 400.0
        base.Height <- 600.0

        Program.mkProgram Update.init (Update.update this tcs) Views.main
        |> Program.withHost this
        //|> Program.withConsoleTrace
        |> Program.runWithAvaloniaSyncDispatch (n)

    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
