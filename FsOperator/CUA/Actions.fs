namespace FsOperator
open System
open FSharp.Control
open PuppeteerSharp
open FsResponses
open System.IO
open FsOpCore
open PuppeteerSharp.Input


module Actions =


    let actionToString action =
        try
            match action with
            | Click p -> $"click({p.x},{p.y},{p.button})"
            | Scroll p -> $"scroll {p.scroll_x},{p.scroll_y}@{p.x},{p.y}"
            | Double_click p -> $"dbl_click({p.x},{p.y})"
            | Keypress p -> $"keys {p.keys}"
            | Move p -> $"move({p.x},{p.y})"
            | Screenshot -> "screenshot"
            | Type p -> $"type {p.text}"
            | Wait  -> "wait"
            | Drag p ->
                let s = p.path.Head
                let t = List.last p.path
                $"drag {s.x},{s.y} -> {t.x},{t.y}"
        with ex ->
            debug $"Error in actionToString: %s{ex.Message}"
            sprintf "%A" action

    type RequestAction =
        | Btn of MouseButton
        | Back
        | Forward
        | Wheel
        | Unknown
    let mouseButton = function
        | Buttons.Left          -> Btn MouseButton.Left
        | Buttons.Middle        -> Btn MouseButton.Middle
        | Buttons.Right         -> Btn MouseButton.Right
        | "back" | "Back"       -> Back
        | "forward" | "Forward" -> Forward
        | "wheel"   | "Wheel"   -> Wheel
        | x -> Log.info $"cannot use '{x}' button"; Unknown



    let perform (driver:IUIDriver) (action:Action)  =
        async {
            match action with
            | Click p ->
                match mouseButton p.button with
                | Btn btn when btn = MouseButton.Left ->
                    do! driver.click(p.x, int p.y,FsOperator.MouseButton.Left)
                | Btn btn -> Log.info $"Did not use {btn} button (as it may cause issues on web pages)"
                | Back -> do! driver.goBack()
                | Forward -> do! driver.goForward()
                | Wheel -> do! driver.wheel(p.x,p.y)
                | Unknown -> do! Async.Sleep(500) //model is trying to use a button that is not supported
            | Scroll p ->
                do! driver.scroll (p.x,p.y) (p.scroll_x,p.scroll_y)
            | Keypress p -> do! driver.pressKeys p.keys
            | Type p -> do! driver.typeText p.text
            | Wait  ->  do! Async.Sleep(2000)
            | Screenshot -> ()
            | Move p -> do! driver.move(p.x,p.y)
            | Double_click p -> do! driver.doubleClick(p.x,p.y)
            | Drag p ->
                let s = p.path.Head
                let t = List.last p.path
                do! driver.dragDrop (s.x,s.y) (t.x,t.y)
                Log.info $"Drag and drop from {s} to{t}"
        }

    let rec doAction retryCount driver (action:Action) =
        async {
            try
                do! perform driver action
            with ex ->
                Log.exn (ex,"Error in doAction")
                do! Async.Sleep(500)
                if retryCount < 2 then
                    return! doAction (retryCount + 1) driver action
                else
                    Log.warn "Unable to perform action after retrying"
        }
