namespace FsOperator
#nowarn "57"
#nowarn "40"
open Avalonia.FuncUI
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

module Nav = 
    let  nav : Ref<TextBox> = ref Unchecked.defaultof<_>

[<AbstractClass; Sealed>]
type Views =    
    static member navigationBar model dispatch = 
        TextBox.create [
            TextBox.init (fun x -> Nav.nav.Value <- x)
            Grid.row 0
            TextBox.text (model.url.ToString())
            TextBox.borderThickness 1.0
            TextBox.borderBrush Brushes.LightBlue
            TextBox.verticalAlignment VerticalAlignment.Top
            TextBox.horizontalAlignment HorizontalAlignment.Stretch
            TextBox.onKeyDown (fun e -> 
                if e.Key = Avalonia.Input.Key.Enter then
                    if Nav.nav.Value<> Unchecked.defaultof<_> then
                        let url = Nav.nav.Value.Text
                        if Uri.IsWellFormedUriString(url, UriKind.Absolute) then                            
                            dispatch (SetUrl url)
                        else
                            debug($"Invalid URL: {url}")
                    else
                        debug("URL is empty")            
            )
            TextBox.margin 2.0
        ]

    static member webview model dispatch = 
        WebView.create [
            Grid.row 1 
            WebView.url model.url
            WebView.init (fun wv -> 
                model.webview.Value <- Some wv
                wv.WebViewCreated.Add(fun args -> 
                    task {
                        try
                            let! pw = Playwright.CreateAsync()
                            let! browser = pw.Chromium.ConnectOverCDPAsync("http://localhost:9222")                            
                            dispatch (BrowserConnected browser)
                        with ex ->                                                                         
                            debug (sprintf "%A" ex)
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
            Grid.rowSpan 2
            Grid.rowDefinitions "30,30,*,40,*"
            Grid.children [
                ToggleSwitch.create [
                    Grid.row 0 
                    ToggleSwitch.isEnabled model.browser.IsSome
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
                    TextBlock.text "Output"
                    TextBlock.horizontalAlignment HorizontalAlignment.Left
                    TextBlock.verticalAlignment VerticalAlignment.Top
                    TextBlock.fontSize 14.
                    TextBlock.fontWeight FontWeight.Bold
                    TextBlock.margin (Thickness(leftMargin,10.,0.,0.))
                ]
                Button.create [
                    Grid.row 3
                    Button.content "\u2715"
                    Button.tip "Clear output"
                    Button.onClick (fun _ -> ())//dispatch ClearOutput)
                    Button.margin (Thickness(5.))
                    Button.horizontalAlignment HorizontalAlignment.Right
                    Button.verticalAlignment VerticalAlignment.Top                    
                    Button.fontSize 10.
                ]
                TextBlock.create  [
                    Grid.row 4
                    TextBlock.text model.output
                    TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                    TextBlock.verticalAlignment VerticalAlignment.Stretch
                    TextBlock.fontSize 14.                    
                    TextBlock.background Brushes.DarkSlateBlue
                    TextBlock.margin (Thickness(leftMargin,0.,2.,6.))
                ]
            ]
        ]

    static member main model dispatch =
        DockPanel.create [               
            DockPanel.children [
                Expander.create [
                    Expander.margin (Thickness(2.))
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
                    Grid.rowDefinitions "35,*"
                    Grid.columnDefinitions "*,300"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch                    
                    Grid.children [
                        Views.navigationBar model dispatch
                        Views.webview model dispatch
                        Views.instructions model dispatch
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
    
