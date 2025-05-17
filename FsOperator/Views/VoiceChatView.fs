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
                            TextBlock.text "Voice Asst. Generated Instructions"
                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                            TextBlock.verticalAlignment VerticalAlignment.Top
                            TextBlock.fontSize 11.
                            TextBlock.fontWeight FontWeight.Bold
                            TextBlock.margin (Thickness(leftMargin,1.,0.,0.))
                        ]
                        TextBlock.create [
                            TextBlock.text (RunState.voiceSysMsg model.runState)
                            TextBlock.textWrapping TextWrapping.Wrap
                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                            TextBlock.verticalAlignment VerticalAlignment.Stretch
                            TextBlock.multiline true
                            TextBlock.textAlignment TextAlignment.Left
                            TextBlock.background Brushes.Transparent
                            TextBlock.margin (Thickness(leftMargin,40.,2.,2.))
                            TextBlock.fontSize 14.
                        ]
                        Button.create [
                            Button.isEnabled (BrowserMode.isReady model.browserMode  
                                              && (csMode.IsCM_Init 
                                                  || csMode.IsCM_Voice
                                                  || csState.IsCUA_Init))
                            Button.margin (Thickness(0.,0.,1.,2.))
                            Button.fontSize 11.
                            Button.content (if csState.IsCUA_Init then "Start Task" else "Cancel Task" )
                            Button.onClick (fun _ -> dispatch VoicChat_StartStop) 
                            Button.horizontalAlignment HorizontalAlignment.Right
                            Button.verticalAlignment VerticalAlignment.Top
                        ]
                    ]
                ]
                ScrollViewer.create [
                    Grid.row 1
                    ScrollViewer.init (fun s -> 
                        Cache.scrollViewVoice.Value <- s
                        s.SizeChanged.Add(fun x -> s.ScrollToEnd()))
                    ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                    ScrollViewer.content (
                        ChatHistoryView.chatHistory 
                            leftMargin 
                            model 
                            (RunState.voiceChatMessages model.runState)
                            dispatch)                            
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
