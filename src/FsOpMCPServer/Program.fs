namespace FsOpMCPServer
open System
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Avalonia.FuncUI.Hosts
open Elmish
open Avalonia.FuncUI.Elmish
open Avalonia
open Avalonia.Themes.Fluent
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI
open Avalonia.Input
open Microsoft.AspNetCore.Builder


module MainView = 
    open Avalonia.Controls
    open Avalonia.Layout
    open Avalonia.FuncUI.DSL
    open Avalonia.Media
    let main model dispatch =
        DockPanel.create [
            DockPanel.margin 5
            DockPanel.children [
                TextBlock.create [
                    DockPanel.dock Dock.Top
                    TextBlock.text "Running"
                ]
                TextBlock.create [
                    DockPanel.dock Dock.Bottom
                    TextBlock.text $"Count: {model.count}"
                ]
                ListBox.create [
                    ListBox.dataItems model.bills
                    ListBox.itemTemplate (
                        DataTemplateView<Bill>.create (fun (bill:Bill) -> 
                            TextBlock.create [
                                TextBlock.text bill.FileName
                                TextBlock.fontSize 12.
                                TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                TextBlock.maxWidth 255.
                                TextBlock.verticalAlignment VerticalAlignment.Top
                                TextBlock.textWrapping TextWrapping.Wrap
                                TextBlock.multiline true
                            ]
                    ))
                ]
            ]
        ]

module Update = 
 
    let init() = {count=0; bills=[]}, Cmd.none
    let update (window: HostWindow) msg model =
        match msg with
        | NoOp -> model, Cmd.none
        | GotBill bill ->
            let newBills = bill :: model.bills
            {model with count = model.count + 1; bills = newBills}, Cmd.none
  
type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "MCP Server Running"
        base.Width <- 400.0
        base.Height <- 600.0

        Program.mkProgram Update.init (Update.update this) MainView.main
        |> Program.withSubscription Subscriptions.subscriptions
        |> Program.withHost this
        //|> Program.withConsoleTrace        
        |> Program.runWithAvaloniaSyncDispatch ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add (FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
        this.Styles.Load "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"    
        this.AttachDevTools(Diagnostics.DevToolsOptions(Gesture=KeyGesture(Key.F12)))

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            
            let win = MainWindow()
            //U.initNotfications(win)
            //win.Closing.Add(fun _ -> Connection.disconnect())
            //DevToolsExtensions.AttachDevTools(this)
            desktopLifetime.MainWindow <- win
        | _ -> ()


module Pgm =
    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder()
        builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithTools<JiraTools>()
                .WithTools<PortOutTools>()
                |> ignore

        builder.Logging.AddConsole(fun options ->
            options.LogToStandardErrorThreshold <- LogLevel.Trace
        ) |> ignore

        builder.Services.AddSingleton<HttpClient>(fun _ ->
            let client = new HttpClient(BaseAddress = Uri("https://api.weather.gov"))
            client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue("weather-tool", "1.0"))
            client
        ) |> ignore

        let app = builder.Build()
        app.UseHttpsRedirection() |> ignore
        app.MapMcp() |> ignore
        app.RunAsync() |> ignore // |> Async.AwaitTask |> Async.RunSynchronously

        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
    #if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
    #endif
            .StartWithClassicDesktopLifetime(args)
            
