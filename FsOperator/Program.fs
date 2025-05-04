namespace FsOperator

open Elmish
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI
open Avalonia.FuncUI.Elmish
open Avalonia.FuncUI.Hosts
open System
open Microsoft.Extensions.DependencyInjection
open Avalonia.Input
open Xilium.CefGlue


type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "Computer Use Agent"
        base.Width <- 800.0
        base.Height <- 500.0

        Program.mkProgram Update.init (Update.update this) Views.main
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
                match Nav.connection.Value with 
                | Some conn -> 
                    try
                        conn.Disconnect()
                        conn.CloseAsync().WaitAsync(TimeSpan.FromSeconds 1.0).Wait()      
                        Async.RunSynchronously(async {CefRuntime.Shutdown()},1000) 
                    with ex ->
                        debug $"Error closing connection: {ex.Message}"
                | None -> ())
        | _ -> ()

module Program =
    [<EntryPoint; STAThread>]
    let main(args: string[]) =
        System.Environment.SetEnvironmentVariable("PW_CHROMIUM_ATTACH_TO_OTHER","1")
        //WebViewControl.WebView.Settings.LogFile <- @"e:\\s\\log.txt"
        WebViewControl.WebView.Settings.AddCommandLineSwitch("remote-debugging-port", "9222")
        WebViewControl.WebView.Settings.AddCommandLineSwitch("remote-allow-origins", "http://localhost:9222")
        WebViewControl.WebView.Settings.AddCommandLineSwitch("no-sandbox", "")
        //System.IO.File.WriteAllText(@"e:\s\pageinject.js", Scripts.indicatorScript_page)
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            
            .UseSkia()            
#if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
            
