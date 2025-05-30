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
type TextChatView =    

    static member chat model dispatch =
        let leftMargin = 10.
        let cuaMode = model.taskState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let chatMode = model.taskState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        Grid.create [
            Grid.column 1
            Grid.rowSpan 2
            Grid.rowDefinitions "1*,2*"
            Grid.children [
                Panel.create [
                    Grid.row 0
                    Panel.children [
                        TextBlock.create  [                            
                            TextBlock.text "Instructions"
                            TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                            TextBlock.verticalAlignment VerticalAlignment.Top
                            TextBlock.fontSize 11.
                            TextBlock.fontWeight FontWeight.Bold                            
                            TextBlock.margin (Thickness(leftMargin,1.,0.,0.))
                        ]
                        TextBox.create [
                            TextBox.text (model.opTask.textModeInstructions)
                            TextBox.textWrapping TextWrapping.Wrap
                            TextBox.horizontalAlignment HorizontalAlignment.Stretch
                            TextBox.verticalAlignment VerticalAlignment.Stretch
                            TextBox.multiline true
                            TextBox.acceptsReturn true
                            TextBox.textAlignment TextAlignment.Left
                            TextBox.background Brushes.Transparent
                            TextBox.borderThickness 2.
                            TextBox.watermark "Enter instructions here or load a task to start"
                            TextBox.margin (Thickness(leftMargin,40.,2.,2.))
                            TextBox.fontSize 14.
                            TextBox.onTextChanged (fun t -> dispatch (OpTask_SetTextInstructions t))
                        ]
                        Button.create [
                            Button.isEnabled (model.browserMode.IsBM_Ready 
                                              && (chatMode.IsCM_Init 
                                                  || chatMode.IsCM_Text
                                                  || cuaMode.IsCUA_Init))
                            Button.margin (Thickness(0.,0.,1.,2.))
                            Button.background Brushes.Transparent
                            Button.fontSize 11.
                            Button.content (if cuaMode.IsCUA_Init then Icons.start else Icons.stop )
                            Button.tip (if cuaMode.IsCUA_Init then "Start task" else "Cancel task")
                            Button.onClick (fun _ -> dispatch TextChat_StartStopTask) 
                            Button.horizontalAlignment HorizontalAlignment.Right
                            Button.verticalAlignment VerticalAlignment.Top
                        ]
                        if chatMode.IsCM_Text && not cuaMode.IsCUA_Init then 
                            Button.create [
                                Button.isEnabled (cuaMode.IsCUA_Loop || cuaMode.IsCUA_Pause)
                                Button.background Brushes.Transparent
                                Button.margin (Thickness(0.,2.,25.,2.))
                                Button.fontSize 14.
                                Button.fontFamily Icons.iconFont
                                Button.tip "Stop and report"
                                Button.content Icons.report
                                Button.onClick (fun _ -> dispatch Chat_StopAndSummarize) 
                                Button.horizontalAlignment HorizontalAlignment.Right
                                Button.verticalAlignment VerticalAlignment.Top
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
                        match model.taskState with 
                        | Some rs when rs.cuaState.IsCUA_Pause && rs.chatMode.IsCM_Text -> 
                            Panel.create [
                                DockPanel.dock Dock.Top
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
                                        Button.content Icons.send
                                        Button.onClick (fun _ -> dispatch Chat_Resume) 
                                        Button.horizontalAlignment HorizontalAlignment.Right
                                        Button.verticalAlignment VerticalAlignment.Bottom
                                    ]
                                ]
                            ]
                        | _ -> ()                        
                        ScrollViewer.create [
                            ScrollViewer.init(fun s -> Cache.scrollViewText.Value <- s)
                            ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                            ScrollViewer.content (
                                ChatHistoryView.chatHistory 
                                    leftMargin 
                                    model 
                                    (TaskState.textChatMessages model.taskState)
                                    dispatch)                            
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
