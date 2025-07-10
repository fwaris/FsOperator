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
open AvaloniaGraphControl
open Avalonia.Controls.Templates
open FsOpPlanEditor.DragDrop2

[<AbstractClass; Sealed>]
type Views =

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

    static member iconButton (content:string) clickHandler (tip:string) =
        Button.create [
            Button.width 14.
            Button.height 14.
            Button.fontSize 10.0
            Button.verticalAlignment VerticalAlignment.Center
            Button.tip tip
            Button.background Brushes.Transparent
            Button.padding 0.0
            Button.content content
            Button.onClick clickHandler
        ]

    static member nodeMenu (n:ONode) dispatch =
        Border.create [
            Border.borderBrush Brushes.DarkCyan
            Border.background Brushes.DarkSlateBlue
            Border.borderThickness 1.0
            Border.cornerRadius 3.0
            Border.padding 2.0
            Border.child (
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        if not n.IsSeq then
                            Views.iconButton Icons.sequence.Value (fun _ -> dispatch (ConvertToSequence n)) "Convert to 'sequence' node"
                        if not n.IsChoose then
                            Views.iconButton Icons.forkedArrow (fun _ -> dispatch (ConvertToChoose n)) "Convert to 'choose' node"
                        Views.iconButton Icons.plus (fun _ -> dispatch AddTask n) "Add a 'task' node"
                        Views.iconButton Icons.minus (fun _ -> dispatch (DeleteNode n))  "Delete this node"
                        Views.iconButton Icons.edit (fun _ -> dispatch (EditNode n))  "Edit node"
                    ]
                ]
            )
        ]

    static member choose (c:Choose) dispatch =
        Border.create [
            Border.borderBrush Brushes.DarkCyan
            Border.borderThickness 1.0
            Border.cornerRadius 3.0
            Border.padding 2.0
            Border.child (
                StackPanel.create [
                    StackPanel.children [
                        TextSticker.create [
                            TextSticker.shape TextSticker.Shapes.Diamond
                            TextSticker.dataContext (c.description |> Option.map (shorten 30) |> Option.defaultValue "")
                        ]
                        Views.nodeMenu (ONode.Choose c) dispatch
                    ]
                ])
        ]

    static member sequence (s:FsOpCore.Seq) dispatch =
        Border.create [
            Border.borderBrush Brushes.DarkCyan
            Border.borderThickness 1.0
            Border.cornerRadius 3.0
            Border.padding 2.0
            Border.child (
                StackPanel.create [
                    StackPanel.children [
                        TextSticker.create [
                            TextSticker.shape TextSticker.Shapes.Rectangle
                            TextSticker.dataContext (s.description |> Option.map (shorten 30) |> Option.defaultValue "")
                        ]
                        Views.nodeMenu (ONode.Seq s) dispatch
                    ]
                ])
        ]

    static member toolbar (model:Model) dispatch =
        DockPanel.create [
            Grid.row 0
            DockPanel.children [
                Button.create [Button.content Icons.plus; DockPanel.dock Dock.Left; Button.onClick (fun _ -> dispatch AddTask)]
                StackPanel.create [
                    DockPanel.dock Dock.Right;
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        Button.create [Button.content Icons.cancel; Button.onClick (fun _ -> dispatch Close)]
                        Button.create [Button.content Icons.accept; Button.onClick (fun _ -> dispatch Save)]
                    ]
                ]
                TextBlock.create []
            ]
        ]

    static member planFlow (model:Model) dispatch =
        let btns = model.nodes |> List.map (fun t -> Views.task t dispatch :> IView)
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
                        GraphPanel.create [
                            GraphPanel.dataTemplates (
                                let ds = DataTemplates()
                                ds.AddRange(
                                    [
                                        DataTemplateView<ONode>.create (fun data ->
                                            Border.create [
                                                Control.allowDrop true
                                                Control.onDragEnter(fun e -> e.DragEffects <-
                                                                                match e.Data.Get(DataFormats.Text) with
                                                                                | :? OTask as t -> DragDropEffects.Copy
                                                                                | _ -> DragDropEffects.None)
                                                Control.onDrop(fun e -> match e.Data.Get(DataFormats.Text) with
                                                                        | :? OTask as t -> dispatch (Dropped t)
                                                                        | _             -> ())
                                                Border.child (

                                                    match data with
                                                    | ONode.Leaf t -> Views.task t dispatch :> IView
                                                    | ONode.Choose c -> Views.choose c dispatch
                                                    | ONode.Seq s -> Views.sequence s dispatch
                                                )
                                            ])
                                    ])
                                ds)
                            GraphPanel.background Brushes.DarkTurquoise
                            GraphPanel.layoutMethods GraphPanel.LayoutMethods.SugiyamaScheme
                            GraphPanel.graph (model.root |> Update.edges |> Update.graph)
                        ]
                    ]
                ]
            )
        ]

    static member main (model:Model) dispatch =
        DockPanel.create [
            DockPanel.children [
                Grid.create [
                    Grid.rowDefinitions "50,*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        Views.toolbar model dispatch
                        Views.planFlow model dispatch
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

        Program.mkProgram Update.init (Update.update this tcs) Views.main
        |> Program.withHost this
        //|> Program.withConsoleTrace
        |> Program.runWithAvaloniaSyncDispatch (plan)


    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
