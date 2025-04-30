namespace FsOperator

open Elmish
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI
open Avalonia.FuncUI.Elmish
open Avalonia.FuncUI.Hosts
open System.IO
open Avalonia.Threading
open Microsoft.Extensions.Configuration
open Avalonia.WebView.Desktop
open Avalonia.Diagnostics
open Avalonia.Logging
open System


type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "WebView Test"
        base.Width <- 800.0
        base.Height <- 500.0

        Program.mkProgram Update.init (Update.update this) Views.main
        |> Program.withHost this
        //|> Program.withSubscription Update.subscriptions
        |> Program.withConsoleTrace        
        |> Program.runWithAvaloniaSyncDispatch ()


type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add (FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
        this.Styles.Load "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            let win = MainWindow()
            //U.initNotfications(win)
            //win.Closing.Add(fun _ -> Connection.disconnect())
            DevToolsExtensions.AttachDevTools(this)
            desktopLifetime.MainWindow <- win
        | _ -> ()

module Program =

    [<EntryPoint; STAThread>]
    let main(args: string[]) =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            //.UseSkia()
            .UseDesktopWebView()
#if DEBUG
            .LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
