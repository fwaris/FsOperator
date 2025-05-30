namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia
open Avalonia.Svg.Skia
open Avalonia.Controls.Shapes
open Avalonia.FuncUI
open Avalonia.Labs.Lottie
open Avalonia.Labs.Lottie.Ext
open FsOpCore
open Avalonia.Platform

[<AbstractClass; Sealed>]
type BrowserView =

    static member navigationBar model dispatch = 
        let csState = model.taskState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let actionBg = 
            if model.isFlashing 
            then Brushes.DarkOrange 
            elif (TaskState.cuaMode model.taskState).IsCUA_Pause 
            then  Brushes.DarkRed
            else Brushes.DarkSlateBlue
        Border.create [
            Grid.row 0
            Border.borderThickness 1.0
            Border.margin 2.0
            Border.borderBrush Brushes.LightBlue
            Border.verticalAlignment VerticalAlignment.Top
            Border.horizontalAlignment HorizontalAlignment.Stretch
            Border.child (        
                Grid.create [
                    Grid.columnDefinitions "65*,35*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.verticalAlignment VerticalAlignment.Stretch
                    Grid.children [                    
                        TextBox.create [
                            Grid.column 0
                            TextBox.init (fun x -> Cache.nav.Value <- x)
                            TextBox.text (model.opTask.url)
                            TextBox.borderThickness 0.
                            TextBox.margin 5
                            TextBox.watermark "Enter URL here"
                            TextBox.verticalAlignment VerticalAlignment.Center
                            TextBox.horizontalAlignment HorizontalAlignment.Stretch
                            TextBox.onKeyDown (fun e -> 
                                if e.Key = Avalonia.Input.Key.Enter then
                                    if Cache.nav.Value<> Unchecked.defaultof<_> then
                                        let url = Cache.nav.Value.Text
                                        dispatch (OpTask_SetUrl url)
                                    else
                                        debug("URL is empty")            
                            )
                        ]
                        TextBlock.create [
                                Grid.column 1
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                TextBlock.background actionBg
                                TextBlock.text model.action
                                TextBlock.margin (Thickness(1,1,5,1))
                                TextBlock.padding 3
                        ]

                    ]

                ]
            )    
        ]
        