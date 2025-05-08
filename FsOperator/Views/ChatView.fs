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

    static member chatHistory leftMargin model dispatch = 
        ListBox.create [
            ListBox.margin (Thickness(leftMargin,2.,2.,5.))
            ListBox.dataItems (model.runState |> Option.map _.chatHistory |> Option.defaultValue [])                            
            ListBox.styles [ FsStyles.initStyle() ]
            ListBox.itemTemplate (
                DataTemplateView<ChatMsg>.create (fun (msg: ChatMsg) -> 
                    Panel.create [
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
        let csState = model.runState |> Option.map (fun rs -> rs.chatState) |> Option.defaultValue ChatState.CS_Init
        let csMode = model.runState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        Grid.create [
            Grid.column 1
            Grid.rowSpan 2
            Grid.rowDefinitions "1*,2*"
            Grid.children [
                TabControl.create [
                    Grid.row 0
                    TabControl.viewItems [
                        //text mode
                        TabItem.create [
                            TabItem.header (
                                Panel.create [
                                    Panel.children [
                                        TextBlock.create [
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.text "Text"
                                            TextBlock.fontSize 14.
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(leftMargin,1.,50.,0.))
                                        ]
                                        if ((csState.IsCS_Prompt || csState.IsCS_Loop) && csMode.IsCM_Text) then 
                                            Lottie.create [
                                                Grid.row 0
                                                Lottie.margin (Thickness(10.,0.,0.,0.))
                                                Lottie.verticalAlignment VerticalAlignment.Center
                                                Lottie.horizontalAlignment HorizontalAlignment.Right
                                                Lottie.path Lottie.defaultPath.Value
                                                Lottie.height 50.
                                            ]
                                    ]
                                ]
                            )
                            TabItem.content (
                                Panel.create [
                                    Grid.row 1
                                    Panel.children [
                                        TextBlock.create  [                            
                                            TextBlock.text "Instructions"
                                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                            TextBlock.verticalAlignment VerticalAlignment.Top
                                            TextBlock.fontSize 14.
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(leftMargin,1.,0.,0.))
                                        ]
                                        TextBox.create [
                                            TextBox.text model.instructions
                                            TextBox.textWrapping TextWrapping.Wrap
                                            TextBox.horizontalAlignment HorizontalAlignment.Stretch
                                            TextBox.verticalAlignment VerticalAlignment.Stretch
                                            TextBox.multiline true
                                            TextBox.acceptsReturn true
                                            TextBox.textAlignment TextAlignment.Left
                                            TextBox.background Brushes.Transparent
                                            TextBox.borderThickness 2.
                                            TextBox.margin (Thickness(leftMargin,30.,2.,37.))
                                            TextBox.fontSize 14.
                                            TextBox.onTextChanged (fun t -> dispatch (SetInstructions t))
                                        ]
                                        Button.create [
                                            Button.isEnabled (model.initialized  && (csMode.IsCM_Init || csMode.IsCM_Text))
                                            Button.margin (Thickness(0.,0.,1.,2.))
                                            Button.content (if csState.IsCS_Init then "Start Task" else "Cancel Task" )
                                            Button.onClick (fun _ -> dispatch TextChat_StartStopTask) 
                                            Button.horizontalAlignment HorizontalAlignment.Right
                                            Button.verticalAlignment VerticalAlignment.Bottom
                                        ]
                                    ]
                                ]
                            )
                        ]
                        
                        //voice mode
                        TabItem.create [
                            TabItem.header (
                                Panel.create [
                                    Panel.children [
                                        TextBlock.create [
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.text "Voice"
                                            TextBlock.fontSize 14.
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (Thickness(leftMargin,1.,50.,0.))
                                        ]
                                        if ((csState.IsCS_Prompt || csState.IsCS_Loop) && csMode.IsCM_Voice) then
                                            Lottie.create [
                                                Grid.row 0
                                                Lottie.margin (Thickness(10.,0.,0.,0.))
                                                Lottie.verticalAlignment VerticalAlignment.Center
                                                Lottie.horizontalAlignment HorizontalAlignment.Right
                                                Lottie.path Lottie.defaultPath.Value
                                                Lottie.height 50.
                                        ]
                                    ]
                                ]
                            )
                            TabItem.content(
                                Panel.create [
                                    Panel.children [
                                        Button.create [
                                            Button.margin 2
                                            Button.verticalAlignment VerticalAlignment.Top
                                            Button.content (if csState.IsCS_Init then "Start Task" else "StopTask")
                                            Button.isEnabled (model.initialized && (csMode.IsCM_Init || csMode.IsCM_Voice))
                                            Button.onClick (fun _ -> dispatch VoicChat_StartStop)
                                        ]
                                    ]
                                ]
                            )
                        ]
                    ]
                ]
                DockPanel.create [
                    Grid.row 1
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
                        match model.runState with 
                        | Some rs when rs.chatState.IsCS_Prompt -> 
                            Panel.create [
                                DockPanel.dock Dock.Bottom
                                Panel.margin 2
                                Panel.children [
                                    TextBox.create [
                                        TextBox.text rs.question
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
                        | _ -> ()                        
                        ScrollViewer.create [
                            ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                            ScrollViewer.content (ChatView.chatHistory leftMargin model dispatch)                            
                        ]
                    ]
                ]
                GridSplitter.create [
                    Grid.row 1
                    Grid.columnSpan 1
                    GridSplitter.verticalAlignment VerticalAlignment.Top
                    GridSplitter.width 50.
                    GridSplitter.horizontalAlignment HorizontalAlignment.Stretch
                    GridSplitter.background Brushes.DarkGray                                
                ]
            ]
        ]

