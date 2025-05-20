namespace FsOperator
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Avalonia.Input
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.AspNetCore.Builder
open FsOpCore
open Elmish
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI
open Avalonia.FuncUI.Elmish
open Avalonia.FuncUI.Hosts

type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- C.WIN_TITLE
        base.Width <- 400.0
        base.Height <- 600.0

        Program.mkProgram Update.init (Update.update this) MainView.main
        |> Program.withHost this
        |> Program.withSubscription Update.subscriptions        
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
            desktopLifetime.ShutdownRequested.Add (fun (s:ShutdownRequestedEventArgs) ->                 
                Update.mailbox.Writer.TryComplete() |> ignore
                Async.RunSynchronously(Browser.shutdown(),1000))
        | _ -> ()

module Program =
    [<EntryPoint; STAThread>]
    let main(args: string[]) =
        let builder = WebApplication.CreateBuilder()
        builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithTools<McpTools.JiraTools>()
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

        System.Environment.SetEnvironmentVariable("PW_CHROMIUM_ATTACH_TO_OTHER","1")
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
            
