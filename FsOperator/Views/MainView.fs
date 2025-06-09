namespace FsOperator
open System
open FsOpCore
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

    static member defaultVoicePromptInfo = 
        Button.create [
            Button.horizontalAlignment HorizontalAlignment.Right
            Button.tip "Default Voice Asst. Instructions"
            Button.verticalAlignment VerticalAlignment.Top
            Button.margin (Thickness(2.,30,2,2))
            Button.content "\u2139"
            Button.background Brushes.Transparent
            Button.flyout (
                Flyout.create [
                    Flyout.showMode FlyoutShowMode.Standard
                    Flyout.content(
                        Grid.create [
                            Grid.maxWidth 300.
                            Grid.maxHeight 300.
                            Grid.rowDefinitions "25,*"
                            Grid.children [
                                TextBlock.create [
                                    Grid.row 0
                                    TextBlock.text "Default Voice Asst. Instructions"
                                    TextBlock.fontSize 11
                                    Control.margin 2
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                ScrollViewer.create [
                                    Grid.row 1
                                    ScrollViewer.maxHeight 350.
                                    ScrollViewer.maxWidth 350.
                                    ScrollViewer.content (
                                        SelectableTextBlock.create [
                                            TextBlock.padding 2.
                                            TextBlock.background Brushes.SlateBlue
                                            TextBlock.multiline true
                                            TextBlock.margin 2
                                            TextBlock.textWrapping TextWrapping.Wrap
                                            TextBlock.text OpTask.defaultVoicePrompt
                                        ]
                                    )
                                ]
                            ]
                        ]
                    )
                ]
            )           
        ]

    static member instructionEdit model dispatch = 
        let cache = Cache.opTaskTexts
        Grid.create [
            Grid.rowDefinitions "*,*,*,*"
            Grid.columnDefinitions "100,*"
            Grid.width 400.
            Grid.maxHeight 700.
            Grid.children [                
                TextBlock.create [
                    Grid.row 0
                    Grid.column 0
                    TextBlock.text "Description"
                    Control.margin 2
                    Control.verticalAlignment VerticalAlignment.Center
                    Control.horizontalAlignment HorizontalAlignment.Right
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[0].Value <- x)
                    Grid.row 0
                    Grid.column 1
                    TextBox.text model.opTask.description
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
                    TextBox.text (OpTask.targetToString model.opTask.target)
                ]
                TextBlock.create [
                    Grid.row 2
                    Grid.column 0
                    TextBlock.text "Text Prompt"                    
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
                    TextBox.minHeight 150.
                    TextBox.text model.opTask.textModeInstructions
                ]
                Panel.create [
                    Grid.row 3
                    Grid.column 0
                    Panel.children [
                        MainView.defaultVoicePromptInfo
                        TextBlock.create [
                            TextBlock.text "Voice Prompt"
                            Control.margin 2
                            Control.horizontalAlignment HorizontalAlignment.Right                    
                        ]
                    ]
                ]
                TextBox.create [
                    TextBox.init (fun x -> cache.[3].Value <- x)
                    Grid.row 3
                    Grid.column 1
                    TextBox.margin 3
                    TextBox.minHeight 150.
                    TextBox.text model.opTask.voiceAsstInstructions
                    TextBox.watermark "Leave blank to use default voice asst. instructions"
                    TextBox.acceptsReturn true
                    TextBox.multiline true
                ]
                Button.create [
                    Grid.row 3
                    Grid.column 0
                    Button.verticalAlignment VerticalAlignment.Bottom
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Grid.columnSpan 2
                    Control.margin 2
                    Button.content "Apply"
                    Button.onClick (fun _ -> 
                        let description = cache.[0].Value.Text
                        let target = cache.[1].Value.Text
                        let textPrompt = cache.[2].Value.Text
                        let voicePrompt = cache.[3].Value.Text |> fixEmpty                        
                        let pTarget = OpTask.parseTarget target
                        {   id = ""
                            description = description
                            target = pTarget
                            textModeInstructions = textPrompt
                            voiceAsstInstructions = voicePrompt 
                        }
                        |> OpTask_Update
                        |> dispatch
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
    
    static member mainMenu model dispatch = 
            Menu.create [
                Menu.horizontalAlignment HorizontalAlignment.Right
                Menu.verticalAlignment VerticalAlignment.Top
                Menu.margin 2.
                Menu.viewItems [
                    MenuItem.create [                           
                        MenuItem.header "☰"
                        MenuItem.viewItems [
                            MenuItem.create [
                                MenuItem.header "Clear All"
                                MenuItem.onClick (fun _ -> dispatch OpTask_Clear)
                            ]
                            MenuItem.create [
                                MenuItem.header "Load Task"
                                MenuItem.onClick (fun _ -> dispatch OpTask_Load)
                            ]
                            MenuItem.create [
                                MenuItem.header "Save Task As"
                                MenuItem.onClick (fun _ -> dispatch OpTask_SaveAs)
                            ]
                            MenuItem.create [
                                MenuItem.header "Load Sample"
                                MenuItem.viewItems (
                                    OpTask.Samples.allSamples 
                                    |> List.map (fun t -> 
                                        MenuItem.create [
                                            MenuItem.header $"{t.id}: {t.description}"
                                            MenuItem.onClick (fun _ -> dispatch (OpTask_LoadSample t))
                                        ]
                                    )
                                )
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
                //edit flyout button
                Button.create [
                    Button.content "\u270E"
                    Button.fontFamily "Segoe UI"                    
                    Button.tip "Edit task"
                    Button.background Brushes.Transparent
                    Button.flyout(
                        Flyout.create [
                            Flyout.placement PlacementMode.LeftEdgeAlignedTop
                            Flyout.showMode FlyoutShowMode.Standard
                            Flyout.content (MainView.instructionEdit model dispatch)
                        ]
                    )
                ]
                Button.create [
                    Button.content "\U0001F4BE"
                    Button.fontFamily "Segoe UI Emoji"
                    Button.tip "Save task"
                    Button.background Brushes.Transparent
                    Button.hotKey (Input.KeyGesture(Input.Key.S, modifiers=Input.KeyModifiers.Control))
                    Button.onClick (fun _ -> dispatch OpTask_Save)
                ]
                //rest of the toolbar buttons
                MainView.mainMenu model dispatch
            ]
        ]

    static member contentWrapper model dispatch =
        let leftMargin = 10.
        let csState = model.taskState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let csMode = model.taskState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        Panel.create [
            Grid.row 1
            Panel.horizontalAlignment HorizontalAlignment.Stretch
            Panel.verticalAlignment VerticalAlignment.Stretch
            Panel.children [
                ChatView.chat model dispatch
                MainView.toolBar model dispatch
            ]
        ]    

    static member logPanel model dispatch =
        Expander.create [
            Expander.margin (Thickness(1.))
            Expander.dock Dock.Right
            Expander.expandDirection ExpandDirection.Left
            Expander.verticalAlignment VerticalAlignment.Stretch
            Expander.horizontalAlignment HorizontalAlignment.Right
            Expander.content (
                Panel.create [          
                    Panel.children [
                        ListBox.create [
                            ListBox.width 300.
                            ListBox.margin (Thickness(5,12.,5.,5.))
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
                        Button.create [
                            Button.content "Clear"
                            Button.fontSize 11.
                            Button.background Brushes.Transparent
                            Button.onClick (fun _ -> dispatch Log_Clear)
                            Button.horizontalAlignment HorizontalAlignment.Right
                            Button.verticalAlignment VerticalAlignment.Top
                            Button.margin 2
                        ]
                    ]
                ]
            )
        ]

    static member main model dispatch =
        DockPanel.create [               
            DockPanel.children [
                MainView.logPanel model dispatch
                MainView.statusBar model dispatch
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
    
