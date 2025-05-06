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
open Avalonia.Platform


module FsStyles = 
    let initStyle() =
        let s = Style(fun x -> x.Is<ListBoxItem>())
        s.Setters.Add(Setter(ListBoxItem.MinHeightProperty, 10.))
        s.Setters.Add(Setter(ListBoxItem.MarginProperty, Thickness(1.)))
        s.Setters.Add(Setter(ListBoxItem.PaddingProperty, Thickness(1.)))
        s :> IStyle

[<AbstractClass; Sealed>]
type ChatView =    
    static member messageView model dispatch msg (margin:Thickness)  = 
        match msg with 
        | User content -> 
            TextBlock.create [
                TextBlock.text content
                TextBlock.background Brushes.DarkSlateBlue
                TextBlock.textWrapping TextWrapping.Wrap
                TextBlock.padding 1
                TextBlock.multiline true
                TextBlock.margin margin
            ]
            :> Types.IView
        | Assistant msg -> 
            TextBlock.create [
                TextBlock.text msg.content
                TextBlock.background Brushes.DarkSlateGray
                TextBlock.textWrapping TextWrapping.Wrap                                            
                TextBlock.multiline true
                TextBlock.padding 1
                TextBlock.margin margin
            ]
        | Question content ->
            Panel.create [
                Panel.margin 2
                Panel.children [
                    TextBox.create [
                        TextBox.text content
                        //TextBox.isEnabled (model.runState |> Option.map (fun cs -> cs.chatState.IsCS_Prompt ) |> Option.defaultValue false)
                        TextBox.textWrapping TextWrapping.Wrap
                        TextBox.horizontalAlignment HorizontalAlignment.Stretch
                        TextBox.verticalAlignment VerticalAlignment.Stretch
                        TextBox.multiline true
                        TextBox.minHeight 50.
                        TextBox.acceptsReturn true
                        TextBox.textAlignment TextAlignment.Left
                        TextBox.margin 1  
                        TextBox.fontSize 14.
                        TextBox.borderThickness 1.
                        TextBox.onTextChanged (fun t -> dispatch (Chat_UpdateQuestion t))
                        TextBox.margin (Thickness(2.,2.,35.,2.))
                    ]
                    Button.create [
                        Button.margin (Thickness(0.,0.,1.,2.))
                        Button.content "\u27a1"
                        Button.onClick (fun _ -> dispatch Chat_Submit) 
                        Button.horizontalAlignment HorizontalAlignment.Right
                        Button.verticalAlignment VerticalAlignment.Bottom
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


    static member chatHistory leftMargin model dispatch = 
        ListBox.create [
            ListBox.margin (Thickness(leftMargin,2.,2.,5.))
            ListBox.dataItems (model.runState |> Option.map _.chatHistory |> Option.defaultValue [])                            
            ListBox.styles [ FsStyles.initStyle() ]
            ListBox.itemTemplate (
                DataTemplateView<ChatMsg>.create (fun (msg: ChatMsg) -> 
                    Panel.create [
                        //Panel.background (match msg with Assistant _ -> Brushes.DarkSeaGreen | _ -> Brushes.Transparent)

                        Panel.children [
                            ChatView.messageView model dispatch msg (Thickness(1,12,1,1))
                            TextBlock.create [
                                TextBlock.background Brushes.Transparent
                                TextBlock.text (match msg with Assistant _ -> "\u2328" | _ -> "")
                                TextBlock.fontSize 12.                                
                                TextBlock.verticalAlignment VerticalAlignment.Top
                                TextBlock.horizontalAlignment HorizontalAlignment.Left
                                TextBox.margin 0//(Thickness(1.,1.,0.,0.))
                            ]
                        ]
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

