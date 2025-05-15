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
open Avalonia.Markup.Xaml.Styling
open Avalonia.Logging


type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "Computer Use Agent"
        base.Width <- 800.0
        base.Height <- 400.0

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
                Async.RunSynchronously(Connection.shutdown(),1000))
        | _ -> ()

module Program =
    [<EntryPoint; STAThread>]
    let main(args: string[]) =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            //.LogToTrace(LogEventLevel.Debug)            
#endif
            .StartWithClassicDesktopLifetime(args)
            
