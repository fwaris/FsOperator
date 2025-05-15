namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open Avalonia.FuncUI
open Avalonia.Labs.Lottie
open Avalonia.Labs.Lottie.Ext
open Avalonia.Styling
open Avalonia.Platform


[<AbstractClass; Sealed>]
type VoiceChatView =    

    static member chat model dispatch =
        let leftMargin = 10.
        let csState = model.runState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let csMode = model.runState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        Grid.create [
            Grid.column 1
            Grid.rowSpan 2
            Grid.rowDefinitions "1*,2*"
            Grid.children [
                Panel.create [
                    Grid.row 0
                    Panel.children [
                        TextBlock.create  [                            
                            TextBlock.text "Generated Instructions"
                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                            TextBlock.verticalAlignment VerticalAlignment.Top
                            TextBlock.fontSize 10.
                            TextBlock.fontWeight FontWeight.Bold
                            TextBlock.margin (Thickness(leftMargin,1.,0.,0.))
                        ]
                        TextBlock.create [
                            TextBlock.text (Instructions.getTextChat model.instructions)
                            TextBlock.textWrapping TextWrapping.Wrap
                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                            TextBlock.verticalAlignment VerticalAlignment.Stretch
                            TextBlock.multiline true
                            TextBlock.textAlignment TextAlignment.Left
                            TextBlock.background Brushes.Transparent
                            TextBlock.margin (Thickness(leftMargin,30.,2.,37.))
                            TextBlock.fontSize 14.
                        ]
                        Button.create [
                            Button.isEnabled (model.browserState.state.IsBST_Ready  && (csMode.IsCM_Init || csMode.IsCM_Voice))
                            Button.margin (Thickness(0.,0.,1.,2.))
                            Button.content (if csState.IsCUA_Init then "Start Task" else "Cancel Task" )
                            Button.onClick (fun _ -> dispatch TextChat_StartStopTask) 
                            Button.horizontalAlignment HorizontalAlignment.Right
                            Button.verticalAlignment VerticalAlignment.Bottom
                        ]
                    ]
                ]
                ScrollViewer.create [
                    Grid.row 1
                    ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                    ScrollViewer.content (ChatHistoryView.chatHistory leftMargin model dispatch)                            
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
