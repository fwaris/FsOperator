namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia

[<AbstractClass; Sealed>]
type TextChatView =    

    static member chat model dispatch =
        let leftMargin = 10.
       
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
                            //Button.isEnabled (model.flow.IsFL_Init)
                            Button.margin (Thickness(0.,0.,1.,2.))
                            Button.background Brushes.Transparent
                            Button.fontSize 11.
                            Button.content (if model.flow.IsFL_Init then Icons.start else Icons.stop )
                            Button.tip (if model.flow.IsFL_Init  then "Start task" else "Cancel task")
                            Button.onClick (fun _ -> dispatch Flow_StartStop)
                            Button.horizontalAlignment HorizontalAlignment.Right
                            Button.verticalAlignment VerticalAlignment.Top
                        ]
                        if model.flow.IsFL_Flow || model.flow.IsFL_Flow_Summarizing then 
                            Button.create [
                                Button.background Brushes.Transparent
                                Button.margin (Thickness(0.,2.,25.,2.))
                                Button.fontSize 14.
                                Button.fontFamily Icons.iconFont
                                Button.tip "Stop and report"
                                Button.content Icons.report
                                Button.isEnabled (model.flow.IsFL_Flow)
                                Button.onClick (fun _ -> dispatch Flow_StopAndSummarize) 
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
                        match model.flow with 
                        | FL_Flow f when f.chat.prompt ->
                            Panel.create [
                                DockPanel.dock Dock.Top
                                Panel.margin 2
                                Panel.children [
                                    TextBox.create [
                                        TextBlock.init (fun t -> Cache.textQuestion.Value <- t)
                                        //TextBox.text (Cache.textQuestion.Value.Text)
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
                                        //TextBox.onTextChanged (fun t -> dispatch (Chat_UpdateQuestion t))
                                        TextBox.margin (Thickness(2.,2.,35.,2.))
                                    ]
                                    Button.create [
                                        Button.margin (Thickness(0.,0.,1.,2.))
                                        Button.content Icons.send
                                        Button.onClick (fun _ -> dispatch (Flow_Resume Cache.textQuestion.Value.Text)) 
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
                                    (model.flow.messages())
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
