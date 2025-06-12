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
type ChatView =    

    static member chat model dispatch =
        let leftMargin = 10.
        let csState = model.taskState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let csMode = model.taskState |> Option.map (fun rs -> rs.chatMode) |> Option.defaultValue ChatMode.CM_Init
       
        TabControl.create [
            TabControl.horizontalAlignment HorizontalAlignment.Stretch
            TabControl.verticalAlignment VerticalAlignment.Stretch
            Grid.row 1
            TabControl.viewItems [
                //text mode
                TabItem.create [
                    TabItem.horizontalAlignment HorizontalAlignment.Stretch
                    TabItem.verticalAlignment VerticalAlignment.Stretch
                    TabItem.header (
                        Panel.create [
                            Panel.children [
                                TextBlock.create [
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.text "Text"
                                    TextBlock.fontSize 14.
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.margin (Thickness(leftMargin,1.,30.,0.))
                                ]
                                if model.flow.IsFL_Flow then 
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
                    TabItem.content (TextChatView.chat model dispatch)
                ]
                        
                        
                //voice mode
                TabItem.create [
                    TabItem.horizontalAlignment HorizontalAlignment.Stretch
                    TabItem.verticalAlignment VerticalAlignment.Stretch
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
                                if ((csState.IsCUA_Pause || csState.IsCUA_Loop) && csMode.IsCM_Voice) then
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
                    TabItem.content(VoiceChatView.chat model dispatch)
                ]
            ]
        ]

