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

[<AbstractClass; Sealed>]
type Views =
    static member iconButton (content:string) clickHandler (tip:string) =
        Button.create [
            Button.width 16.
            Button.height 16.
            Button.fontSize 12.0
            Button.verticalAlignment VerticalAlignment.Center
            Button.tip tip
            Button.background Brushes.Transparent
            Button.padding 0.0
            Button.content content
            Button.onClick clickHandler
        ]

    static member nodeMenu (n:ONode) dispatch =
        Border.create [
            DockPanel.dock Dock.Bottom
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
                            let tip = if n.IsLeaf then "Put this task under a 'sequence' node" else "Convert to 'sequence' node"
                            Views.iconButton Icons.ellipsis (fun _ -> dispatch (ConvertToSequence n)) tip
                        if not n.IsChoose then
                            let tip = if n.IsLeaf then "Put this task under a 'choose' node" else "Convert to 'choose' node"
                            Views.iconButton Icons.forkedArrow (fun _ -> dispatch (ConvertToChoose n)) tip
                        if not n.IsLeaf then
                            Views.iconButton Icons.plus (fun _ -> dispatch (AddTask n)) "Add a 'task' node"
                        Views.iconButton Icons.minus (fun _ -> dispatch (DeleteNode n))  "Delete this node"
                        Views.iconButton Icons.edit (fun _ -> dispatch (EditNode n))  "Edit node"
                    ]
                ]
            )
        ]

    static member nodeColor = function ONode.Leaf _ -> Styles.c1 | ONode.Choose _ -> Styles.c2 | _ -> Styles.c3
    static member nodeFontSz = function ONode.Leaf _ -> 15.| _ -> 25.
    static member nodeTip = function ONode.Leaf _ -> "Task" | ONode.Choose _ -> "Choose 1 of n child" | ONode.Seq _ -> "Sequentially execute all children"

    static member dragHeader (n:ONode) dispatch =
        Panel.create [
            DockPanel.dock Dock.Top
            Control.horizontalAlignment HorizontalAlignment.Stretch
            Panel.background Brushes.DarkSlateBlue
            Panel.children [
                TextBlock.create [
                    Control.onPointerPressed (fun e -> e.Handled <- true; dispatch (BeginDrag (e,n)))
                    Control.cursor Cursors.hand
                    Control.height 10.
                    TextBlock.background Textures.grip
                    Control.horizontalAlignment HorizontalAlignment.Stretch
                ]
            ]
        ]

    static member node (n:ONode) dispatch =
        Border.create [
            Border.margin 3.0
            Border.borderThickness 1.0
            Border.borderBrush Brushes.DarkSlateBlue
            Border.background (Views.nodeColor n)
            Border.cornerRadius 3.0
            Border.padding 3.0
            Control.allowDrop true
            Control.onDragEnter(fun e -> e.DragEffects <-
                                            match e.Data.Get(DataFormats.Text) with
                                            | :? OTask as t -> DragDropEffects.Move
                                            | _ -> DragDropEffects.None)
            Control.onDrop(fun e -> match e.Data.Get(DataFormats.Text) with
                                    | :? ONode as d -> dispatch (DroppedNodeOn (d,n))
                                    | _             -> ())
            Border.child (
                DockPanel.create [
                    DockPanel.margin 3.0
                    DockPanel.children [
                        Views.dragHeader n dispatch
                        Views.nodeMenu n dispatch
                        TextBlock.create [
                            TextBlock.fontSize (Views.nodeFontSz n);
                            TextBlock.foreground Brushes.Black
                            TextBlock.tip (Views.nodeTip n)
                            TextBlock.textAlignment TextAlignment.Center
                            TextBlock.text (
                                match n with
                                | ONode.Leaf t -> t.id |> shorten 30
                                | ONode.Choose _ -> Icons.forkedArrow
                                | ONode.Seq _ -> Icons.sequence.Value
                            )
                        ]
                    ]
                ]
            )
        ]

    static member toolbar (model:Model) dispatch =
        DockPanel.create [
            DockPanel.margin 1.0
            Grid.row 0
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Right;
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        Button.create [Button.content Icons.cancel; Button.onClick (fun _ -> dispatch Close)]
                        Button.create [Button.content Icons.accept; Button.onClick (fun _ -> dispatch Save)]
                    ]
                ]
                StackPanel.create [
                    DockPanel.dock Dock.Left;
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        Button.create [
                            Button.isEnabled (not model.undoStack.IsEmpty)
                            Button.tip "Undo"
                            Button.content Icons.undo
                            Button.onClick (fun _ -> dispatch Undo)
                        ]
                        Button.create [
                            Button.tip "Redo"
                            Button.isEnabled (not model.redoStack.IsEmpty)
                            Button.content Icons.redo
                            Button.onClick (fun _ -> dispatch Redo)
                        ]
                    ]
                ]
                TextBlock.create [] //dock filler
            ]
        ]

    static member planFlow (model:Model) dispatch =
        Border.create [
            Border.borderThickness 1.0
            Border.borderBrush Brushes.LightGray
            Border.margin 2.0
            Grid.row 2
            Border.child(
                DockPanel.create [
                    DockPanel.children [
                        TextBlock.create [
                            TextBlock.text "Nodes"
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.dock Dock.Top
                        ]
                        ScrollViewer.create [
                            ScrollViewer.background Brushes.AntiqueWhite
                            ScrollViewer.content (
                                ZoomBorder.create [
                                    ZoomBorder.enablePan true
                                    ZoomBorder.panButton ButtonName.Right
                                    ZoomBorder.child (
                                        GraphPanel.create [
                                            GraphPanel.dataTemplates (
                                                let ds = DataTemplates()
                                                ds.AddRange(
                                                    [
                                                        DataTemplateView<ONode>.create (fun data ->
                                                            Views.node data dispatch
                                                        )
                                                    ])
                                                ds)
                                            GraphPanel.layoutMethods GraphPanel.LayoutMethods.SugiyamaScheme
                                            GraphPanel.graph (model.root |> Update.graph)
                                        ]
                                    )
                                ]
                            )
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
