namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open Avalonia.FuncUI
open Avalonia.Styling

module FsStyles = 

    //styles for ListBoxItem
    let initStyle() =
        let s = Style(fun x -> x.Is<ListBoxItem>())
        s.Setters.Add(Setter(ListBoxItem.MinHeightProperty, 10.))
        s.Setters.Add(Setter(ListBoxItem.MarginProperty, Thickness(1.)))
        s.Setters.Add(Setter(ListBoxItem.PaddingProperty, Thickness(1.)))
        s :> IStyle

[<AbstractClass; Sealed>]
type  ChatHistoryView = 
    static member messageView model dispatch msg (margin:Thickness)  = 
        match msg with 
        | User content -> 
            SelectableTextBlock.create [
                TextBlock.text content
                TextBlock.background Brushes.DarkSlateBlue
                TextBlock.textWrapping TextWrapping.Wrap
                TextBlock.padding 1
                TextBlock.multiline true
                TextBlock.margin margin
            ]
            :> Types.IView
        | Assistant msg -> 
            SelectableTextBlock.create [
                TextBlock.text msg.content
                TextBlock.background Brushes.DarkSlateGray
                TextBlock.textWrapping TextWrapping.Wrap                                            
                TextBlock.multiline true
                TextBlock.padding 1
                TextBlock.margin margin
            ]

    static member chatHistory leftMargin model messages dispatch = 
        ListBox.create [
            ListBox.margin (Thickness(leftMargin,2.,2.,5.))
            ListBox.dataItems (List.rev messages)
            ListBox.styles [ FsStyles.initStyle() ]
            ListBox.itemTemplate (
                DataTemplateView<ChatMsg>.create (fun (msg: ChatMsg) -> 
                    Panel.create [
                        Panel.children [
                            ChatHistoryView.messageView model dispatch msg (Thickness(1,12,1,1))
                            TextBlock.create [
                                TextBlock.background Brushes.Transparent
                                TextBlock.text (match msg with Assistant _ -> "\u2328" | _ -> "")
                                TextBlock.fontSize 12.                                
                                TextBlock.verticalAlignment VerticalAlignment.Top
                                TextBlock.horizontalAlignment HorizontalAlignment.Center
                                TextBox.margin 0//(Thickness(1.,1.,0.,0.))
                            ]
                        ]
                    ]
                )                        
            )   
        ]
