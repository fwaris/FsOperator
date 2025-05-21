namespace FsOperator
open System
open FSharp.Control
open PuppeteerSharp
open FsResponses
open System.IO
open FsOpCore
open PuppeteerSharp.Input


module Actions = 
    let private (=*=) (a:string) (b:string) = a.Equals(b, StringComparison.OrdinalIgnoreCase)

    let actionToString action = 
        try
            match action with
            | Click p -> $"click({p.x},{p.y},{p.button})"
            | Scroll p -> $"scroll({p.scroll_x},{p.scroll_y},{p.x},{p.y})"
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

    let pressKeys (page:IPage) (keys:string list) = 
        task {
            let keys = List.rev keys
            let key,modifiers =  keys.Head, List.rev keys.Tail
            for m in modifiers do 
                do! page.Keyboard.DownAsync(m)
            do! page.Keyboard.PressAsync(key)
            for m in modifiers do 
                do! page.Keyboard.UpAsync(m)
        }

    let perform (action:Action)  =
        async {
            let! page = Browser.page()
            if page.MainFrame = null then failwith "no main frame"
            match action with 
            | Click p -> 
                match mouseButton p.button with
                | Btn btn when btn = MouseButton.Left -> 
                    let opts = ClickOptions(Button = btn)
                    do! page.Mouse.ClickAsync(p.x,p.y, opts) |> Async.AwaitTask
                | Btn btn -> Log.info $"Did not use {btn} button (as it may cause issues on web pages)"
                | Back -> do! page.GoBackAsync() |> Async.AwaitTask |> Async.Ignore
                | Forward -> do! page.GoForwardAsync() |> Async.AwaitTask |> Async.Ignore
                | Wheel -> do! page.Mouse.WheelAsync(p.x,p.y) |> Async.AwaitTask
                | Unknown -> do! Async.Sleep(500) //model is trying to use a button that is not supported
            | Scroll p ->
                do! page.Mouse.MoveAsync(p.x,p.y) |> Async.AwaitTask                    
                //let! _ = page.EvaluateFunctionAsync($"() => window.scrollBy({p.scroll_x}, {p.scroll_y})")  |> Async.AwaitTask                                          
                let! _ = page.EvaluateFunctionAsync("function(x, y) { window.scrollBy(x, y); }", p.scroll_x, p.scroll_y) |> Async.AwaitTask

                ()
            | Keypress p -> 
                let mappeKeys = 
                    p.keys 
                    |> List.map (fun k -> 
                        if k =*= "Enter" then "Enter"                             //Playwright does not support Enter key
                        elif k =*= "space" then " "
                        elif k =*= "backspace" then "Backspace"
                        elif k =*= "ESC" then "Escape"
                        elif k =*= "SHIFT" then "Shift"
                        elif k =*= "CTRL" then "Control"
                        elif k =*= "TAB" then "Tab"
                        elif k =*= "ArrowLeft" then "ArrowLeft"
                        elif k =*= "ArrowRight" then "ArrowRight"
                        elif k =*= "ArrowUp" then "ArrowUp"
                        elif k =*= "ArrowDown" then "ArrowDown"
                        else k)
                do! pressKeys page mappeKeys |> Async.AwaitTask
            | Type p ->
                do! page.Keyboard.TypeAsync(p.text) |> Async.AwaitTask
            | Wait  ->  do! Async.Sleep(2000)
            | Screenshot -> ()
            | Move p -> do! page.Mouse.MoveAsync(p.x,p.y) |> Async.AwaitTask
            | Double_click p -> do! page.Mouse.ClickAsync(p.x,p.y, ClickOptions(Count=2)) |> Async.AwaitTask
            | Drag p ->                     
                let s = p.path.Head
                let t = List.last p.path 
                do! page.Mouse.MoveAsync(s.x,s.y) |> Async.AwaitTask
                do! page.Mouse.DownAsync() |> Async.AwaitTask
                do! page.Mouse.MoveAsync(t.x,t.y,MoveOptions(Steps=10)) |> Async.AwaitTask
                do! page.Mouse.UpAsync() |> Async.AwaitTask
                Log.info $"Drag and drop from {s} to{t}"
            do! page.WaitForNetworkIdleAsync() |> Async.AwaitTask
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
