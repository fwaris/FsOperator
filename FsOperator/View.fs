namespace FsOperator
#nowarn "57"
#nowarn "40"

open System
open WebViewCore
open Avalonia.WebView.Windows.Core
open Microsoft.Web.WebView2.Core
open Microsoft.Playwright
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
    static member webview model dispatch = 
        WebView.create [
            WebView.url (Uri "https://linkedin.com")
            WebView.init (fun wv -> 
                wv.WebViewCreated.Add(fun args -> 
                    task {
                        try
                            let! pw = Playwright.CreateAsync()
                            let! browser = pw.Chromium.ConnectOverCDPAsync("http://localhost:9222")
                            let contex = browser.Contexts.[0]
                            let page = contex.Pages.[0]
                            let! ss = page.ScreenshotAsync()
                            IO.File.WriteAllBytes(@"C:\Users\Faisa\Pictures\Screenshots\playwright.png", ss)                                    
                            dispatch (BrowserConnected pw)
                        with ex ->                                                                         
                            Diagnostics.Debug.WriteLine($"Error: {ex.Message}")
                    }
                    |> ignore
                    ()
                )
                wv.WebViewNewWindowRequested.Add(fun args ->
                    args.UrlLoadingStrategy <- WebViewCore.Enums.UrlRequestStrategy.OpenInWebView
                ))   
        ]

    static member statusBar model dispatch = 
        Border.create [
            Grid.row 1
            Grid.columnSpan 2
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
                            Button.background Brushes.Transparent
                            Button.content (
                                Ellipse.create [
                                    Shapes.Ellipse.tip $"Service connection: "
                                    Shapes.Ellipse.width 10.
                                    Shapes.Ellipse.height 10.
                                    Shapes.Ellipse.margin (Thickness(5.,0.,5.,0.))
                                    Shapes.Ellipse.verticalAlignment VerticalAlignment.Center
                                ])
                            Button.onClick (fun _ -> ())
                        ]
                    ]
                ]
            )
        ]                            

    static member instructions model dispatch =
        let leftMargin = 10.
        Grid.create [
            Grid.column 1
            Grid.rowDefinitions "30,30,*,30,*"
            Grid.children [
                ToggleSwitch.create [
                    Grid.row 0 
                    ToggleSwitch.isEnabled model.playwright.IsSome
                    ToggleSwitch.onChecked (fun _ -> dispatch Start)
                    ToggleSwitch.onUnchecked (fun _ -> dispatch Stop)
                    ToggleSwitch.horizontalAlignment HorizontalAlignment.Left
                    ToggleSwitch.verticalAlignment VerticalAlignment.Center
                    ToggleSwitch.margin (Thickness(leftMargin,2.,2.,2.))
                ]
                TextBlock.create  [
                    Grid.row 1
                    TextBlock.text "Instructions"
                    TextBlock.horizontalAlignment HorizontalAlignment.Left
                    TextBlock.verticalAlignment VerticalAlignment.Top
                    TextBlock.fontSize 14.
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.margin (Thickness(leftMargin,10.,0.,0.))
                ]
                TextBox.create [
                    Grid.row 2
                    TextBox.text model.instructions
                    TextBox.textWrapping TextWrapping.Wrap
                    TextBox.horizontalAlignment HorizontalAlignment.Stretch
                    TextBox.verticalAlignment VerticalAlignment.Stretch
                    TextBox.multiline true
                    TextBox.acceptsReturn true
                    TextBox.textAlignment TextAlignment.Left
                    TextBox.background Brushes.Transparent
                    TextBox.borderThickness 2.
                    TextBox.margin (Thickness(leftMargin,2.,2.,2.))
                    TextBox.fontSize 14.
                    TextBox.onTextChanged (fun t -> dispatch (SetInstructions t))
                ]
                TextBlock.create  [
                    Grid.row 3
                    TextBlock.text "Log"
                    TextBlock.horizontalAlignment HorizontalAlignment.Left
                    TextBlock.verticalAlignment VerticalAlignment.Top
                    TextBlock.fontSize 14.
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.margin (Thickness(leftMargin,10.,0.,0.))
                ]
            ]
        ]

    static member main model dispatch =
        DockPanel.create [               
            DockPanel.children [
                Expander.create [
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
//                    Grid.rowDefinitions "*,30."
                    Grid.columnDefinitions "*,300"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch                    
                    Grid.children [
                        Views.webview model dispatch
                        Views.instructions model dispatch
//                        Views.statusBar model dispatch
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
    
