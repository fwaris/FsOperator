namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open Avalonia.FuncUI

[<AbstractClass; Sealed>]
type MainView =    

    static member statusBar model dispatch = 
        Border.create [
            DockPanel.dock Dock.Bottom
            Grid.row 2
            Grid.columnSpan 2
            Border.clipToBounds true
            Border.horizontalAlignment HorizontalAlignment.Stretch
            Border.verticalAlignment VerticalAlignment.Bottom
            Border.margin 1
            Border.background Brushes.Transparent
            Border.borderThickness 1.0
            Border.borderBrush Brushes.LightBlue                            
            Border.child(
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        Button.create [
                            Button.height 30.
                            Button.content "..."
                            Button.margin (Thickness(2.))
                            Button.onClick(fun _ -> dispatch TestSomething)
                        ]                        
                        TextBlock.create [
                            TextBlock.verticalAlignment VerticalAlignment.Center
                            TextBlock.fontStyle FontStyle.Italic
                            TextBlock.margin (Thickness(10,0,0,0))
                            TextBlock.text (snd model.statusMsg)
                        ]
                    ]
                ]
            )
        ]                            

    static member instructionEdit model dispatch = 
        let cache = Cache.instrEdits
        Grid.create [
            Grid.rowDefinitions "*,*,*,*,*,*"
            Grid.columnDefinitions "100,*"
            Grid.maxWidth 400.
            Grid.maxHeight 700.
            Grid.children [
                TextBlock.create [                    
                    Grid.row 0
                    Grid.column 0
                    TextBlock.text "Id"
                    Control.horizontalAlignment HorizontalAlignment.Right
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.margin 2
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[0].Value <- x)
                    Grid.row 0
                    Grid.column 1
                    Control.margin 2
                    TextBox.text model.opTask.id
                ]
                TextBlock.create [
                    Grid.row 1
                    Grid.column 0
                    TextBlock.text "Description"
                    Control.margin 2
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[1].Value <- x)
                    Grid.row 1
                    Grid.column 1
                    TextBox.text model.opTask.description
                    Control.margin 2
                ]
                TextBlock.create [
                    Grid.row 2
                    Grid.column 0
                    TextBlock.text "Start URL"
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.horizontalAlignment HorizontalAlignment.Right
                    Control.margin 2
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[2].Value <- x)
                    Grid.row 2
                    Grid.column 1
                    Control.margin 2
                    TextBox.text model.opTask.url
                ]
                TextBlock.create [
                    Grid.row 3
                    Grid.column 0
                    TextBlock.text "Text Prompt"                    
                    Control.margin 2
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[3].Value <- x)
                    Grid.row 3
                    Grid.column 1
                    Control.margin 2
                    TextBox.acceptsReturn true
                    TextBox.multiline true
                    TextBox.text model.opTask.textModeInstructions
                ]
                TextBlock.create [
                    Grid.row 4
                    Grid.column 0
                    TextBlock.text "Voice Prompt"
                    Control.margin 2
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[4].Value <- x)
                    Grid.row 4
                    Grid.column 1
                    Control.margin 2
                    TextBox.text model.opTask.voiceAsstInstructions
                    TextBox.acceptsReturn true
                    TextBox.multiline true
                ]
                Button.create [
                    Grid.row 5
                    Grid.column 0
                    Grid.columnSpan 2
                    Control.margin 2
                    Button.content "Apply"
                    Button.onClick (fun _ -> 
                        let id = cache.[0].Value.Text
                        let description = cache.[1].Value.Text
                        let startUrl = cache.[2].Value.Text
                        let textPrompt = cache.[3].Value.Text
                        let voicePrompt = cache.[4].Value.Text
                        dispatch (SetOpTask { id = id; description = description; url = startUrl; textModeInstructions = textPrompt; voiceAsstInstructions = voicePrompt })
                    )
                ]
            ]
        ]
    
    static member mainMenu model dispatch = 
            Menu.create [
                Menu.horizontalAlignment HorizontalAlignment.Right
                Menu.verticalAlignment VerticalAlignment.Top
                Menu.margin 2.
                Menu.viewItems [
                    MenuItem.create [                           
                        MenuItem.header "☰"
                        MenuItem.viewItems [
                            Menu.create [
                                MenuItem.viewItems [
                                    MenuItem.create [
                                        MenuItem.header "Load Task"
                                        ///MenuItem.onClick (fun _ -> "#e74c3c" |> SetColor |> dispatch)
                                    ]
                                    MenuItem.create [
                                        MenuItem.header "Save Task"
                                        ///MenuItem.onClick (fun _ -> "#e74c3c" |> SetColor |> dispatch)
                                    ]
                                ]
                            ]
                        ]
                    ] 
                ]
            ]                         

    static member toolBar model dispatch = 
        StackPanel.create [
            StackPanel.margin 2
            StackPanel.background Brushes.DarkBlue
            StackPanel.orientation Orientation.Horizontal
            StackPanel.horizontalAlignment HorizontalAlignment.Right
            StackPanel.verticalAlignment VerticalAlignment.Top
            StackPanel.children [
                Button.create [
                    Button.content "\u270f"
                    Button.tip "Edit Instruction"
                    Button.background Brushes.Transparent
                    Button.flyout(
                        Flyout.create [
                            Flyout.placement PlacementMode.LeftEdgeAlignedTop
                            Flyout.showMode FlyoutShowMode.Standard
                            Flyout.content (MainView.instructionEdit model dispatch)
                        ]
                    )
                ]
                MainView.mainMenu model dispatch
            ]
        ]

    static member contentWrapper model dispatch =
        let leftMargin = 10.
        let csState = model.runState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let csMode = model.runState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        Panel.create [
            Grid.row 1
            Panel.horizontalAlignment HorizontalAlignment.Stretch
            Panel.verticalAlignment VerticalAlignment.Stretch
            Panel.children [
                ChatView.chat model dispatch
                MainView.toolBar model dispatch
            ]
        ]    

    static member main model dispatch =
        DockPanel.create [               
            DockPanel.children [
                MainView.statusBar model dispatch
                Expander.create [
                    Expander.margin (Thickness(1.))
                    Expander.dock Dock.Right
                    Expander.expandDirection ExpandDirection.Left
                    Expander.verticalAlignment VerticalAlignment.Stretch
                    Expander.horizontalAlignment HorizontalAlignment.Right
                    Expander.content (
                        ListBox.create [
                            ListBox.width 300.
                            ListBox.margin (Thickness(5.,5.,5.,5.))
                            ListBox.dataItems model.log
                            ListBox.itemTemplate (
                                DataTemplateView<string>.create (fun (logEntry:string) -> 
                                    TextBlock.create [
                                        TextBlock.text logEntry
                                        TextBlock.fontSize 12.
                                        TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                        TextBlock.maxWidth 255.
                                        TextBlock.verticalAlignment VerticalAlignment.Top
                                        TextBlock.textWrapping TextWrapping.Wrap
                                        TextBlock.multiline true
                                    ]
                            ))
                        ]
                    )
                ]
                Grid.create [
                    Grid.rowDefinitions "50,*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        BrowserView.navigationBar model dispatch
                        MainView.contentWrapper model dispatch
                    ]
                ]               
            ]
        ]
    
