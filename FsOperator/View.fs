namespace FsOperator
open System
open System.Net.Http
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open WebViewControl
open WebViewControl.Ext
open PuppeteerSharp

module Nav = 
    let  nav : Ref<TextBox> = ref Unchecked.defaultof<_>

    let getWebSocketDebuggerUrl (port: int) : Task<string> =
        task {
            use client = new HttpClient()
            let! response = client.GetStringAsync(sprintf "http://localhost:%d/json/version" port)
            let json = System.Text.Json.JsonDocument.Parse(response)
            let wsUrl = json.RootElement.GetProperty("webSocketDebuggerUrl").GetString()
            return wsUrl
        }

    let connectToBrowser (port: int) : Task<IBrowser> =
        task {
            let port = 9222
            let! wsUrl = getWebSocketDebuggerUrl port

            let options = ConnectOptions(BrowserWSEndpoint = wsUrl)
            let! browser = Puppeteer.ConnectAsync(options)
            return browser
        }


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
            WebView.address model.url
            WebView.init (fun wv -> 
                match model.webview.Value with
                | Some _ -> ()
                | None -> 
                    model.webview.Value <- Some wv
                    wv.Initialized.Add (fun args ->     
                            task {
                                try
                                    let! browser = Nav.connectToBrowser 9222
                                    let! page = ComputerUse.page browser                                    
                                    let vopts = ViewPortOptions(Width=1280,Height=720)
                                    do! page.SetViewportAsync(vopts) |> Async.AwaitTask
                                    let! _ = page.EvaluateFunctionAsync(Scripts.indicatorScript_page) |> Async.AwaitTask
                                    dispatch (BrowserConnected browser)
                                with ex ->                                                                         
                                    debug (sprintf "%A" ex)
                            }
                            |> ignore                            
                            ()
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
    
