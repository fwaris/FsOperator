namespace FsOperator
open System
open Microsoft.Playwright
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open AvaloniaWebView
open AvaloniaWebView.Ext
open Microsoft.Playwright

module Nav = 
    let  nav : Ref<TextBox> = ref Unchecked.defaultof<_>

[<AbstractClass; Sealed>]
type Views =    
    static member navigationBar model dispatch = 
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
                            TextBox.init (fun x -> Nav.nav.Value <- x)
                            TextBox.text (model.url.ToString())
                            TextBox.borderThickness 0.
                            TextBox.margin 5
                            TextBox.verticalAlignment VerticalAlignment.Center
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
                        ]
                        TextBlock.create [
                                Grid.column 1
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                TextBlock.background Brushes.DarkSlateBlue
                                TextBlock.text model.action
                                TextBlock.margin (Thickness(1,1,5,1))
                                TextBlock.padding 3
                        ]
                    ]

                ]
            )    
        ]

    static member webview model dispatch = 
        WebView.create [
            Grid.row 1 
            WebView.url model.url
            WebView.init (fun wv -> 
                match model.webview.Value with
                | Some _ -> ()
                | None -> 
                    model.webview.Value <- Some wv
                    wv.WebViewCreated.Add(fun args -> 
                        task {
                            try
                                let! pw = Playwright.CreateAsync()
                                let opts = BrowserTypeConnectOverCDPOptions()
                                opts.SlowMo <- 100.f
                                let! browser = pw.Chromium.ConnectOverCDPAsync("http://localhost:9222", opts)                            
                                let page = browser.Contexts.[0].Pages.[0]
                                do! page.SetViewportSizeAsync(1280,720) //this seems to be necessary for best results
                                do! page.AddInitScriptAsync(Scripts.indicatorScript_global) |> Async.AwaitTask
                                let! _ = page.EvaluateAsync(Scripts.indicatorScript_page) |> Async.AwaitTask
                                //let! _ = page.EvaluateAsync("()=>drawArrow(100, 100, 50, Math.PI / 2, 2000);")
                                //let x,y = let s = wv.Bounds.Size in int s.Width/2, int s.Height/2
                                //let! _ = page.EvaluateAsync($"() => window.drawClick({x},{y})") |> Async.AwaitTask

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
            Grid.row 2
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
                        TextBlock.create [
                            TextBlock.verticalAlignment VerticalAlignment.Center
                            TextBlock.fontStyle FontStyle.Italic
                            TextBlock.margin (Thickness(10,0,0,0))
                            TextBlock.text model.warning
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
                    ToggleSwitch.isChecked model.runState.IsSome
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
                    Button.content "\u232b"
                    Button.tip "Clear output"
                    Button.onClick (fun _ -> dispatch ClearOutput)
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
                    TextBlock.textWrapping TextWrapping.Wrap
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
                    Grid.rowDefinitions "50,*,33"
                    Grid.columnDefinitions "*,300"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch   
                    Grid.children [
                        Views.navigationBar model dispatch
                        Views.webview model dispatch
                        Views.instructions model dispatch
                        Views.statusBar model dispatch
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
    
