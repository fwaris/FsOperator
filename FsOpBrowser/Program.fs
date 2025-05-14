namespace FsOpBrowser
open FsOpCore
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
open Avalonia.Markup.Xaml.Styling
open Avalonia.Logging

type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "CUA Browser (do not close)"
        base.Width <- 800.0
        base.Height <- 400.0

        Program.mkProgram Main.init (Main.update this) BrowserView.main
        |> Program.withHost this
        |> Program.withSubscription Main.subscriptions
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
                async {
                    Main.tokenSource.CancelAfter(500)
                    //Main.outchannel.Writer.TryWrite(P2PFromClient.Client_Disconnect Main.clientId) |> ignore
                    let! _ = Async.StartChild(async {CefRuntime.Shutdown()}, 1000)
                    do! Async.Sleep(1000)
                }
                |> Async.RunSynchronously
            )
        | _ -> ()

module Program =
    [<EntryPoint; STAThread>]
    let main(args: string[]) =        
        match Array.toList args with 
        | port:: _ -> Main.port <- int port
        | _ -> ()
        //WebViewControl.WebView.Settings.OsrEnabled <- true
        System.Environment.SetEnvironmentVariable("PW_CHROMIUM_ATTACH_TO_OTHER","1")
        //WebViewControl.WebView.Settings.LogFile <- @"e:\\s\\log.txt"
        WebViewControl.WebView.Settings.AddCommandLineSwitch("remote-debugging-port", string C.DEBUG_PORT)
        WebViewControl.WebView.Settings.AddCommandLineSwitch("remote-allow-origins", $"http://localhost:{C.DEBUG_PORT}")
        //WebViewControl.WebView.Settings.AddCommandLineSwitch("auto-open-devtools-for-tabs","")
        WebViewControl.WebView.Settings.AddCommandLineSwitch("no-sandbox", "")
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
            
