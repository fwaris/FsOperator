namespace FsOpPlanEditor
open System
open Elmish
open FsOpCore
open Avalonia.Controls.PanAndZoom
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
open AvaloniaGraphControl
open Avalonia.Controls.Templates
open FsOpPlanEditor.DragDrop2

type EditorDialog(n:ONode,content:IView) as this =
    inherit HostWindow()
    let tcs = new System.Threading.Tasks.TaskCompletionSource<(ONode*ONode) option>()

    do
        base.Title <- "Plan Editor"
        base.Width <- 400.0
        base.Height <- 600.0

        this.Content <- content

    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
    
(*
    id          : string
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Function list
    allowedSec  : int
*)

[<AbstractClass; Sealed>]
type Editors =
    static member taskEdit (n:ONode) dispatch = 
        let cache : Ref<TextBox> list = 
            [for _ in 1 .. ((FSharp.Reflection.FSharpType.GetRecordFields typeof<OTask>).Length - 1) -> //textboxes for all fields except id  
                (ref Unchecked.defaultof<_>)]
        let task = match n with ONode.Leaf t -> t | _ -> failwith "leaf node expected"
        Grid.create [
            Grid.rowDefinitions "*,*,*,*,*"
            Grid.columnDefinitions "100,*"
            Grid.width 400.
            Grid.maxHeight 700.
            Grid.children [                
                TextBlock.create [
                    Grid.row 0
                    Grid.column 0
                    TextBlock.text "Id"
                    Control.margin 2
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[0].Value <- x)
                    Grid.row 0
                    Grid.column 1
                    TextBox.text (string task.id)
                    Control.margin 2
                ]
                TextBlock.create [
                    Grid.row 1
                    Grid.column 0
                    TextBlock.text "URL"
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.horizontalAlignment HorizontalAlignment.Right
                    Control.margin 2
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[1].Value <- x)
                    Grid.row 1
                    Grid.column 1
                    Control.margin 2
                    TextBox.text (task.target.TargetString())
                ]
                TextBlock.create [
                    Grid.row 2
                    Grid.column 0
                    TextBlock.text "Description"
                    Control.margin 2
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[2].Value <- x)
                    Grid.row 2
                    Grid.column 1
                    TextBox.margin 3
                    TextBox.acceptsReturn true
                    TextBox.multiline true
                    TextBox.minHeight 60.
                    TextBox.text task.description
                ]
                TextBlock.create [
                    Grid.row 3
                    Grid.column 0
                    TextBlock.text "CUA Instructions"                    
                    Control.margin 2
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[3].Value <- x)
                    Grid.row 3
                    Grid.column 1
                    TextBox.margin 3
                    TextBox.minHeight 150.
                    TextBox.text (task.cua |> Option.defaultValue "")
                    TextBox.watermark "Leave blank to use default voice asst. instructions"
                    TextBox.acceptsReturn true
                    TextBox.multiline true
                ]
                Button.create [
                    Grid.row 4
                    Grid.column 0
                    Button.verticalAlignment VerticalAlignment.Bottom
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Grid.columnSpan 2
                    Control.margin 2
                    Button.content "Apply"
                    Button.onClick (fun _ -> 
                        let id = cache.[0].Value.Text
                        let target = cache.[1].Value.Text
                        let pTarget = OTaskTarget.parseTarget target
                        let description = cache.[2].Value.Text
                        let cua = cache.[3].Value.Text
                        let task' =
                            {OTask.Create() with 
                                id = id
                                target = pTarget
                                description = description
                                cua = cua |> checkEmpty
                            }
                        if task' <> task then  
                            dispatch (UpdateNode (Some(n, ONode.Leaf task')))
                        )
                ]
            ]
        ]
        |> fun g -> 
            Border.create [
                Border.borderThickness 1.
                Border.padding 5.
                Border.cornerRadius 1.
                Border.borderBrush Brushes.LightBlue
                Border.background Brushes.Transparent
                Border.clipToBounds true
                Border.child g
            ]
        :> IView


    static member nodeEdit (n:ONode) dispatch = 
        match n with 
        | ONode.Leaf _ -> Editors.taskEdit n dispatch
        | ONode.Seq _ -> TextBlock.create [TextBlock.text "seq"]        
        | ONode.Choose _ -> TextBlock.create [TextBlock.text "choose"]
