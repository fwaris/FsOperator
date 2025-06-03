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



    let perform (action:Action)  =
        async {
            let! page = Browser.page()
            do! Browser.clickable()
            if page.MainFrame = null then failwith "no main frame"
            match action with 
            | Click p -> 
                match mouseButton p.button with
                | Btn btn when btn = MouseButton.Left -> 
                    do! Browser.click(p.x, int p.y,FsOperator.MouseButton.Left)                    
                | Btn btn -> Log.info $"Did not use {btn} button (as it may cause issues on web pages)"
                | Back -> do! page.GoBackAsync() |> Async.AwaitTask |> Async.Ignore
                | Forward -> do! page.GoForwardAsync() |> Async.AwaitTask |> Async.Ignore
                | Wheel -> do! Browser.wheel(p.x,p.y) 
                | Unknown -> do! Async.Sleep(500) //model is trying to use a button that is not supported
            | Scroll p ->
                do! Browser.scroll (p.x,p.y) (p.scroll_x,p.scroll_y)
            | Keypress p -> 
                let mappedKeys = Browser.mapKeys p.keys
                do! Browser.pressKeys mappedKeys
            | Type p ->
                do! page.Keyboard.TypeAsync(p.text) |> Async.AwaitTask
            | Wait  ->  do! Async.Sleep(2000)
            | Screenshot -> ()
            | Move p -> do! Browser.move(p.x,p.y)
            | Double_click p -> do! Browser.doubleClick(p.x,p.y)
            | Drag p -> 
                let s = p.path.Head
                let t = List.last p.path 
                do! Browser.dragDrop (s.x,s.y) (t.x,t.y)
                Log.info $"Drag and drop from {s} to{t}"
        }

    let rec doAction retryCount (action:Action) = 
        async {
            try
                do! perform action
            with ex -> 
                do! Browser.closeConnection()
                Log.exn (ex,"Error in doAction")
                do! Async.Sleep(500)
                if retryCount < 2 then 
                    return! doAction (retryCount + 1) action
                else
                    Log.warn "Unable to perform action after retrying"
        }
