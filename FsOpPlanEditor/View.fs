namespace FsOpPlanEditor
open System
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
open FsOpPlanEditor.DragDrop2

[<AbstractClass; Sealed>]
type MainView =

    static member task (t:OTask) dispatch =
        Border.create [
            Border.margin 3.0
            Border.borderThickness 1.0
            Border.borderBrush Brushes.AliceBlue
            Border.cornerRadius 3.0
            Border.onPointerPressed (fun e -> e.Handled <- true; dispatch (BeginDrag (e,t)))
            Border.padding 3.0
            Border.child (
                DockPanel.create [
                    DockPanel.margin 3.0
                    DockPanel.children [
                        Button.create [
                            Button.margin 2.0
                            Button.content Icons.pen
                            Button.tip "Edit task properties"
                            Button.onClick (fun _ -> dispatch (EditTask t))
                            DockPanel.dock Dock.Left
                        ]
                        Button.create [
                            Button.margin 2.0
                            Button.content Icons.minus
                            Button.tip "Remove task"
                            Button.onClick (fun _ -> dispatch (EditTask t))
                            DockPanel.dock Dock.Right
                        ]
                        TextBlock.create [
                            TextBlock.margin 2.
                            TextBlock.verticalAlignment VerticalAlignment.Center
                            TextBlock.width 50.
                            TextBlock.text t.id
                            TextBlock.tip $"Task id: {t.id}"
                        ]
                    ]
                ]
            )
        ]

    static member tasks (model:Model) dispatch =
        let btns = model.tasks |> List.map (fun t -> MainView.task t dispatch :> IView)
        Border.create [
            Border.borderThickness 1.0
            Border.borderBrush Brushes.LightGray
            Grid.row 1
            Border.child(
                DockPanel.create [
                    DockPanel.children [
                        TextBlock.create [
                            TextBlock.text "Tasks"
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.dock Dock.Top
                        ]
                        WrapPanel.create [                         
                            WrapPanel.children btns
                            
                        ]
                    ]
                ]
            )
        ]

    static member toolbar (model:Model) dispatch =
        DockPanel.create [
            Grid.row 0
            DockPanel.children [
                Button.create [Button.content Icons.plus; DockPanel.dock Dock.Left; Button.onClick (fun _ -> dispatch AddTask)]
                Button.create [Button.content "Cancel"; DockPanel.dock Dock.Right; Button.onClick (fun _ -> dispatch Close)]
                Button.create [Button.content "Save"; DockPanel.dock Dock.Right; Button.onClick (fun _ -> dispatch Save)]
            ]
        ]

    static member planFlow (model:Model) dispatch =
        let btns = model.nodes |> List.map (fun t -> MainView.task t dispatch :> IView)
        Border.create [

            Border.borderThickness 1.0
            Border.borderBrush Brushes.LightGray
            Grid.row 2
            Border.child(
                DockPanel.create [
                    DockPanel.children [
                        TextBlock.create [
                            TextBlock.text "Nodes"
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.dock Dock.Top
                        ]
                        View.createGeneric<AvaloniaGraphControl.GraphPanel> [] :> IView        // or IView<MyControl
                        //NodeEditor.Controls.Editor
                        //WrapPanel.create [
                        //    Control.allowDrop true
                        //    Control.onDragEnter(fun e -> e.DragEffects <- 
                        //                                    match e.Data.Get(DataFormats.Text) with 
                        //                                    | :? OTask as t -> DragDropEffects.Copy 
                        //                                    | _ -> DragDropEffects.None)
                        //    Control.onDrop(fun e -> match e.Data.Get(DataFormats.Text) with 
                        //                            | :? OTask as t -> dispatch (Dropped t)
                        //                            | _             -> ())
                        //    WrapPanel.background Brushes.DarkCyan
                        //    WrapPanel.children btns

                        //]
                    ]
                ]
            )
        ]

    static member main (model:Model) dispatch =
        DockPanel.create [
            DockPanel.children [
                Grid.create [
                    Grid.rowDefinitions "50,*,*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        MainView.toolbar model dispatch
                        MainView.tasks model dispatch
                        MainView.planFlow model dispatch
                    ]
                ]
            ]
        ]

type PlanEditor(plan:OPlan) as this =
    inherit HostWindow()
    let tcs = new System.Threading.Tasks.TaskCompletionSource<OPlan option>()

    do
        base.Title <- "Plan Editor"
        base.Width <- 400.0
        base.Height <- 600.0

        Program.mkProgram Update.init (Update.update this tcs) MainView.main
        |> Program.withHost this
        //|> Program.withConsoleTrace
        |> Program.runWithAvaloniaSyncDispatch (plan)


    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
