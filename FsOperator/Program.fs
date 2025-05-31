namespace FsOperator
open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Avalonia.Input
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

    let startHost() = 
        let builder = WebApplication.CreateBuilder()
        builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithTools<McpTools.JiraTools>()
                |> ignore

        builder.Logging.AddConsole(fun options ->
            options.LogToStandardErrorThreshold <- LogLevel.Trace
        ) |> ignore

        builder.Logging.SetMinimumLevel (LogLevel.Information) |> ignore

        builder.Logging.AddFile(fun ctx ->             
            ctx.MaxFileSize <- 10485760
            ctx.BasePath <- "Logs"
            ctx.CounterFormat <- "000"                       
            ctx.Files <- [|
                Karambolo.Extensions.Logging.File.LogFileOptions(
                    Path="default-<counter>.log")
            |]
        )
        |> ignore

        let app = builder.Build()
        app.UseHttpsRedirection() |> ignore        
        app.MapMcp() |> ignore

        //configure logging for the various modules
        FsOpCore.Log.init app.Services
        FsResponses.Log.init app.Services
        RTOpenAI.Api.Log.init app.Services

        app.RunAsync() |> ignore // |> Async.AwaitTask |> Async.RunSynchronously


    let startApp(args) = 
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
            

    [<EntryPoint; STAThread>]
    let main(args: string[]) =      
        System.Environment.SetEnvironmentVariable("PW_CHROMIUM_ATTACH_TO_OTHER","1")
        startHost()
        startApp(args)
