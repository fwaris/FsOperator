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
                        ChatView.chatWrapper model dispatch
                    ]
                ]               
            ]
        ]
    
