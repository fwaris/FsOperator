namespace FsOpTaskRunner
open Elmish
open System
open Avalonia.FuncUI.Elmish
open Avalonia.Layout
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Elmish.ElmishHook
open Avalonia.Threading

module CounterComponent =
    type Model = { count: int }
    type Msg = Increment | Decrement

    let init() = { count = 0 }, Cmd.none

    let update msg model =
        match msg with
        | Increment -> { model with count = model.count + 1 }, Cmd.none
        | Decrement -> { model with count = model.count - 1 }, Cmd.none

    let view model dispatch =
        // ... build UI as above ...
        DockPanel.create [
            DockPanel.children [
                TextBlock.create [
                    TextBlock.text $"{model.count}"
                ]            
            ]
        ]

    let private subscriptions (model: Model) : Sub<Msg> =
        let timerSub (dispatch: Msg -> unit) =
            let invoke() = dispatch Msg.Increment; true
            DispatcherTimer.Run(invoke, TimeSpan.FromMilliseconds 1000.0)

        [ 
                [ nameof timerSub ], timerSub
        ]

    // The Component wrapper: uses useElmish to run the MVU loop internally
    let counterView () : IView =
        Component.create("counterComponent", fun ctx ->
            let model, dispatch = ctx.useElmish (init, update, Program.withSubscription subscriptions)
            // The view renders the current state and dispatch function
            view model dispatch
        )

