namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia

[<AbstractClass; Sealed>]
type MainView =    

    static member statusBar model dispatch = 
        Border.create [
            Grid.row 2
            Grid.columnSpan 2
            Border.clipToBounds true
            Border.horizontalAlignment HorizontalAlignment.Stretch
            Border.verticalAlignment VerticalAlignment.Bottom
            Border.margin 3
            Border.background Brushes.DarkSlateGray
            Border.borderThickness 1.0
            Border.borderBrush Brushes.LightBlue                            
            Border.child(
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children [
                        Button.create [
                            Button.content "Test"
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


    static member main model dispatch =
        DockPanel.create [               
            DockPanel.children [
                Expander.create [
                    Expander.margin (Thickness(2.))
                    Expander.dock Dock.Right
                    Expander.expandDirection ExpandDirection.Left
                    Expander.verticalAlignment VerticalAlignment.Stretch
                    Expander.horizontalAlignment HorizontalAlignment.Right
                    Expander.content (
                        ListBox.create [
                            ListBox.width 300.
                            ListBox.margin (Thickness(5.,5.,5.,5.))
                            ListBox.dataItems model.log
                        ]
                    )
                ]
                Grid.create [
                    Grid.rowDefinitions "50,*,33"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        BrowserView.navigationBar model dispatch
                        ChatView.chat model dispatch
                        MainView.statusBar model dispatch
                        GridSplitter.create [
                            Grid.column 1
                            Grid.rowSpan 2
                            GridSplitter.verticalAlignment VerticalAlignment.Center
                            GridSplitter.height 50.
                            GridSplitter.horizontalAlignment HorizontalAlignment.Left                                
                            GridSplitter.background Brushes.DarkGray                                
                        ]
                    ]
                ]               
            ]
        ]
    
