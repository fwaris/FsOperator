namespace FsOperator

#nowarn "57"
#nowarn "40"
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open Avalonia.FuncUI.Types
open AvaloniaWebView
open AvaloniaWebView.Ext

[<AbstractClass; Sealed>]
type Views =
    static member main model dispatch =
                WebView.create [
                    WebView.url (Uri "https://linkedin.com")
                    WebView.init (fun wv -> 
                        wv.WebViewNewWindowRequested.Add(fun args ->
                            args.UrlLoadingStrategy <- WebViewCore.Enums.UrlRequestStrategy.OpenInWebView
                        ))   
                ]
            //root view
        //DockPanel.create [                
        //    DockPanel.children [
        //        WebView.create [
        //            WebView.url (Uri "https://linkedin.com")
        //            WebView.init (fun wv -> 
        //                wv.WebViewNewWindowRequested.Add(fun args ->
        //                    args.UrlLoadingStrategy <- WebViewCore.Enums.UrlRequestStrategy.OpenInWebView
        //                ))   
        //        ]
        //        //Grid.create [
        //        //    Grid.rowDefinitions "150.,*,30."
        //        //    Grid.columnDefinitions "*,*"
        //        //    Grid.horizontalAlignment HorizontalAlignment.Stretch
        //        //    Grid.children [
        //        //        Border.create [
        //        //            Grid.row 2
        //        //            Grid.columnSpan 2
        //        //            Border.horizontalAlignment HorizontalAlignment.Stretch
        //        //            Border.verticalAlignment VerticalAlignment.Bottom
        //        //            Border.margin 3
        //        //            Border.background Brushes.DarkSlateGray
        //        //            Border.borderThickness 1.0
        //        //            Border.borderBrush Brushes.LightBlue                            
        //        //            Border.child(
        //        //                StackPanel.create [
        //        //                    StackPanel.orientation Orientation.Horizontal
        //        //                    StackPanel.children [
        //        //                        Button.create [
        //        //                            Button.background Brushes.Transparent
        //        //                            Button.content (
        //        //                                Ellipse.create [
        //        //                                    Shapes.Ellipse.tip $"Service connection: "
        //        //                                    Shapes.Ellipse.width 10.
        //        //                                    Shapes.Ellipse.height 10.
        //        //                                    Shapes.Ellipse.margin (Thickness(5.,0.,5.,0.))
        //        //                                    Shapes.Ellipse.verticalAlignment VerticalAlignment.Center
        //        //                                ])
        //        //                            Button.onClick (fun _ -> ())
        //        //                        ]
        //        //                    ]
        //        //                ]
        //        //            )
        //        //        ]                            
        //        //        GridSplitter.create [
        //        //            Grid.column 1
        //        //            Grid.rowSpan 3
        //        //            GridSplitter.verticalAlignment VerticalAlignment.Center
        //        //            GridSplitter.height 50.
        //        //            GridSplitter.horizontalAlignment HorizontalAlignment.Left                                
        //        //            GridSplitter.background Brushes.DarkGray                                
        //        //        ]
        //        //    ]
        //        //]
        //    ]
        //]

