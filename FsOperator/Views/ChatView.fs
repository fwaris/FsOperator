namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open WebViewControl
open WebViewControl.Ext
open Avalonia.Controls.Shapes
open Avalonia.FuncUI
open Avalonia.Labs.Lottie
open Avalonia.Labs.Lottie.Ext
open Avalonia.Styling

module FsStyles = 
    let initStyle() =
        let s = Style(fun x -> x.Is<ListBoxItem>())
        s.Setters.Add(Setter(ListBoxItem.MinHeightProperty, 10.))
        s.Setters.Add(Setter(ListBoxItem.MarginProperty, Thickness(1.)))
        s.Setters.Add(Setter(ListBoxItem.PaddingProperty, Thickness(1.)))
        s :> IStyle


[<AbstractClass; Sealed>]
type ChatView =    
    static member chatHistory leftMargin model dispatch = 
        ListBox.create [
            ListBox.margin (Thickness(leftMargin,2.,2.,2.))
            ListBox.dataItems (model.runState |> Option.map _.chatHistory |> Option.defaultValue [])
                            
            ListBox.styles [ FsStyles.initStyle() ]
            ListBox.itemTemplate (
                DataTemplateView<ChatMsg>.create (fun (msg: ChatMsg) -> 
                    match msg with 
                    | User content -> 
                        TextBlock.create [
                            TextBlock.text content
                            TextBlock.background Brushes.DarkSlateBlue
                            TextBlock.textWrapping TextWrapping.Wrap
                            TextBlock.padding 1
                            TextBlock.multiline true
                            TextBlock.margin 1
                        ]
                        :> Types.IView
                    | Assistant msg -> 
                        TextBlock.create [
                            TextBlock.text msg.content
                            TextBlock.background Brushes.DarkSlateGray
                            TextBlock.textWrapping TextWrapping.Wrap                                            
                            TextBlock.multiline true
                            TextBlock.padding 1
                            TextBlock.margin 1
                        ]
                    | Question content ->
                        StackPanel.create [
                            StackPanel.orientation Orientation.Vertical
                            StackPanel.horizontalAlignment HorizontalAlignment.Stretch
                            StackPanel.margin 2
                            StackPanel.children [
                                TextBox.create [
                                    TextBox.text content
                                    TextBox.isEnabled (model.runState |> Option.map (fun cs -> cs.chatState.IsCS_Prompt ) |> Option.defaultValue false)
                                    TextBox.textWrapping TextWrapping.Wrap
                                    TextBox.horizontalAlignment HorizontalAlignment.Stretch
                                    TextBox.verticalAlignment VerticalAlignment.Stretch
                                    TextBox.multiline true
                                    TextBox.acceptsReturn true
                                    TextBox.textAlignment TextAlignment.Left
                                    TextBox.margin 1                                                    
                                    TextBox.fontSize 14.
                                    TextBox.background Brushes.DarkSeaGreen
                                    TextBox.borderThickness 1.
                                    TextBox.onTextInput (fun t -> dispatch (Chat_UpdateQuestion t.Text))
                                ]
                                Button.create [
                                    Button.margin 1
                                    Button.content "\u27a1"
                                    Button.onClick (fun _ -> dispatch Chat_Submit) 
                                    Button.horizontalAlignment HorizontalAlignment.Stretch

                                ]
                            ]
                        ]
                    | Placeholder -> 
                        Lottie.create [
                            Lottie.width 10.
                            Lottie.height 10.
                            Lottie.repeatCount -1
                            Lottie.path Lottie.defaultPath.Value
                        ]
                )                        
            )
        ]


    static member chat model dispatch =
        let leftMargin = 10.
        Grid.create [
            Grid.column 1
            Grid.rowSpan 2
            Grid.rowDefinitions "30,30,1*,1*"
            Grid.children [
                ToggleSwitch.create [
                    Grid.row 0 
                    ToggleSwitch.isEnabled model.initialized
                    ToggleSwitch.onChecked (fun _ -> dispatch Start)
                    ToggleSwitch.onUnchecked (fun _ -> dispatch Stop)
                    ToggleSwitch.isChecked model.runState.IsSome
                    ToggleSwitch.horizontalAlignment HorizontalAlignment.Left
                    ToggleSwitch.verticalAlignment VerticalAlignment.Center
                    ToggleSwitch.margin (Thickness(leftMargin,2.,2.,2.))
                ]
                TextBlock.create  [
                    Grid.row 1
                    TextBlock.text "Instructions"
                    TextBlock.horizontalAlignment HorizontalAlignment.Left
                    TextBlock.verticalAlignment VerticalAlignment.Top
                    TextBlock.fontSize 14.
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.margin (Thickness(leftMargin,10.,0.,0.))
                ]
                TextBox.create [
                    Grid.row 2
                    TextBox.text model.instructions
                    TextBox.textWrapping TextWrapping.Wrap
                    TextBox.horizontalAlignment HorizontalAlignment.Stretch
                    TextBox.verticalAlignment VerticalAlignment.Stretch
                    TextBox.multiline true
                    TextBox.acceptsReturn true
                    TextBox.textAlignment TextAlignment.Left
                    TextBox.background Brushes.Transparent
                    TextBox.borderThickness 2.
                    TextBox.margin (Thickness(leftMargin,2.,2.,2.))
                    TextBox.fontSize 14.
                    TextBox.onTextChanged (fun t -> dispatch (SetInstructions t))
                ]
                DockPanel.create [
                    Grid.row 3
                    DockPanel.children [
                        TextBlock.create  [
                            DockPanel.dock Dock.Top
                            TextBlock.text "Chat"
                            TextBlock.horizontalAlignment HorizontalAlignment.Left
                            TextBlock.verticalAlignment VerticalAlignment.Top
                            TextBlock.fontSize 14.
                            TextBlock.fontWeight FontWeight.Bold
                            TextBlock.margin (Thickness(leftMargin,10.,0.,0.))
                        ]
                        ScrollViewer.create [
                            ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                            ScrollViewer.content (ChatView.chatHistory leftMargin model dispatch)
                            
                        ]
                    ]
                ]
                GridSplitter.create [
                    Grid.row 3
                    Grid.columnSpan 1
                    GridSplitter.verticalAlignment VerticalAlignment.Top
                    GridSplitter.width 50.
                    GridSplitter.horizontalAlignment HorizontalAlignment.Stretch
                    GridSplitter.background Brushes.DarkGray                                
                ]
            ]
        ]

