﻿namespace FsOperator
open FsOpCore
open Microsoft.Playwright
open SkiaSharp
open System.Threading

module PlaywrightDriver =
    let _connection : Ref<IBrowser option> = ref None
    let _waitHandle : Ref<ManualResetEvent option> = ref None
    let _prevUrl : Ref<string option> = ref None

    let edgePath() =
        let path =
            if isWindows() then
                Some @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
            elif isMac() then
                Some @"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            else
                None
        match path with
        | Some p when System.IO.File.Exists(p) -> Some p
        | _ -> None

    let launch (handle:WaitHandle) (launchHandle:ManualResetEvent) =
        async {
            try
                use! playwright = Playwright.CreateAsync() |> Async.AwaitTask

                let browserOptions = BrowserTypeLaunchOptions(
                        Headless = false,
                        ExecutablePath = (edgePath() |> Option.defaultValue null)
                    )
                let! browser = playwright.Chromium.LaunchAsync(browserOptions) |> Async.AwaitTask
                let! page = browser.NewPageAsync() |> Async.AwaitTask
                do! page.SetViewportSizeAsync(1280, 768) |> Async.AwaitTask //for OpenAI CUA 1280x768 is needed
                page.SetDefaultTimeout(30000f)
                launchHandle.Set() |> ignore
                _connection.Value <- Some browser
                match _prevUrl.Value with
                | Some url -> do! page.GotoAsync(url) |> Async.AwaitTask |> Async.Ignore
                | None -> ()
                let! r = Async.AwaitWaitHandle handle //need to wait on launch thread otherwise the browser closes.
                ()
            with ex ->
                Log.exn (ex,"Error in launch")
                return raise ex
        }

    let shutdown() =
        async {
            try
                try
                    match _waitHandle.Value with  Some w -> w.Set() |> ignore | None -> ()
                    match _connection.Value with Some conn -> conn.CloseAsync() |> ignore| None -> ()
                with ex ->
                    Log.exn ( ex,"Error in shutdown")
            finally
                _waitHandle.Value <- None
                _connection.Value <- None
        }

    let connection () =
        async {
            match _connection.Value with
            | Some conn when conn.IsConnected -> return conn
            | _ ->
                let! c = Async.StartChild(shutdown(),1000)
                do! c
                _waitHandle.Value <- Some (new ManualResetEvent(false))
                use whLaunch = new ManualResetEvent(false)
                do Async.Start(launch _waitHandle.Value.Value whLaunch)
                let! r = Async.AwaitWaitHandle(whLaunch, 30000)
                if r then
                    Log.info "browser launched"
                    return _connection.Value.Value
                else
                    Log.info "browser launch failed"
                    return failwith "browser launch failed"
        }

    let waitForIdle (page:IPage) =
        async {
            //let loadState = LoadState.NetworkIdle
            let loadState = LoadState.DOMContentLoaded
            let opts = PageWaitForLoadStateOptions()
            opts.Timeout <- 1000.f
            do! page.WaitForLoadStateAsync(loadState,options=opts) |> Async.AwaitTask
        }

    let isProperUrl (url:string) =
        url.Trim().StartsWith("http", System.StringComparison.InvariantCultureIgnoreCase)

    let page () =
        async {
            let! browser = connection()
            browser.Contexts.[0].Pages
            |> Seq.iteri (fun i p ->
                let vs = p.ViewportSize
                Log.info $"page {i} {vs.Width}x{vs.Height}"
            )
            let sortedPages =
                browser.Contexts.[0].Pages
                |> Seq.toList
                |> List.rev
                |> List.sortByDescending (fun p -> p.ViewportSize.Width * p.ViewportSize.Height)

            let page = sortedPages.Head

            Log.info "got pages; waiting for network idle ..."
            let! c = Async.StartChild(waitForIdle page, 1500)
            try do! c with ex -> Log.info $"waitForIdle failed"
            if isProperUrl page.Url then
                _prevUrl.Value <- Some page.Url
            return page
        }

    let pageDown() = async {
        let! page = page()
        let! _ =  page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)") |> Async.AwaitTask
        return ()
    }


    let click(x:int,y:int, btn:FsOperator.MouseButton) = async{
        let! page = page()
        let btn =
            match btn with
            | FsOperator.MouseButton.Left -> MouseButton.Left
            | FsOperator.MouseButton.Middle -> MouseButton.Middle
            | FsOperator.MouseButton.Right -> MouseButton.Right
        let opts = MouseClickOptions(Button = btn)
        do! page.Mouse.ClickAsync(float32 x, float32 y, opts) |> Async.AwaitTask
    }

    let doubleClick(x:int,y:int) = async{
        let! page = page()
        do! page.Mouse.DblClickAsync(float32 x, float32 y) |> Async.AwaitTask
    }

    let wheel(x:int,y:int) = async{
        let! page = page()
        do! page.Mouse.WheelAsync(float32 x, float32 y) |> Async.AwaitTask
    }

    let move(x:int,y:int) = async{
        let! page = page()
        do! page.Mouse.MoveAsync(float32 x, float32 y) |> Async.AwaitTask
    }

    let scroll (x,y) (scrollX:int,scrollY:int) = async{
        let! page = page()
        do! page.Mouse.MoveAsync(float32 x, float32 y) |> Async.AwaitTask
        let js = $"() => window.scrollBy({scrollX}, {scrollY});"

        let! r = page.EvaluateAsync(js) |> Async.AwaitTask
        let i = 1
        ()
    }

    let mapKeys keys =
        keys
        |> List.map (fun k ->
            if k =*= "Enter" then "Enter"
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
            elif k =*= "ALT" then "Alt"
            elif k =*= "ALTGR" then "AltGraph"
            elif k =*= "META" then "Meta"
            elif k =*= "PAGEUP" then "PageUp"
            elif k =*= "PAGEDOWN" then "PageDown"
            elif k =*= "HOME" then "Home"
            elif k =*= "END" then "End"
            elif k =*= "INSERT" then "Insert"
            elif k =*= "DELETE" then "Delete"
            else k)

    let private pressEsc() =
        async {
            try
                let! browser = connection()
                for page in browser.Contexts.[0].Pages do
                    do! page.Keyboard.PressAsync("Escape") |> Async.AwaitTask
            with ex -> ()
        }

    let pressKeys (keys:string list) =
        async {
            let keys = mapKeys keys
            if keys = ["Escape"] then
                do! pressEsc()
            else
                let! page = page()
                let keys = List.rev keys
                let key,modifiers =  keys.Head, List.rev keys.Tail
                for m in modifiers do
                    do! page.Keyboard.DownAsync(m) |> Async.AwaitTask
                do! page.Keyboard.PressAsync(key) |> Async.AwaitTask
                for m in modifiers do
                    do! page.Keyboard.UpAsync(m) |> Async.AwaitTask
        }

    let dragDrop (sX,sY) (tX,tY) =
        async {
            let! page = page()
            do! move(sX,sY)
            do! page.Mouse.DownAsync() |> Async.AwaitTask
            let moveOpts = MouseMoveOptions(Steps=10)
            do! page.Mouse.MoveAsync(float32 tX, float32 tY,moveOpts) |> Async.AwaitTask
            do! page.Mouse.UpAsync() |> Async.AwaitTask
        }

    let closeConnection() =
        async {
            match _connection.Value with
            | None -> ()
            | Some conn ->
                do! conn.CloseAsync() |> Async.AwaitTask
                _connection.Value <- None
        }

    let postUrl (url:string) =
        async {
            let! page = page()
            let! _ = page.GotoAsync(url) |> Async.AwaitTask
            //do! page.BringToFrontAsync() |> Async.AwaitTask
            ()
        }

    let snapshot() =
        async {
            let! page = page()
            Log.info $"taking snapshot of {page.Url}"
            let opts = PageScreenshotOptions()
            opts.Animations <- ScreenshotAnimations.Disabled
            opts.FullPage <- true
            let! image = page.ScreenshotAsync() |> Async.AwaitTask
            Log.info $"done snapshot"
            let bmp = SKBitmap.Decode(image)
            let imgUrl = FsResponses.RUtils.toImageUri image
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(homePath.Value, @"screenshot.png"), image)
            return imgUrl,(int bmp.Width, int bmp.Height)
        }


    let launchExternal() =
            async {
            let! browser = connection()
            return ()
        }

    let clickableObjScript = """() => {
                const clickableSelectors = [
                    'a[href]',
                    'button',
                    '[role="button"]',
                    '[onclick]',
                    '[tabindex]',
                    '[type="button"]',
                    '[type="submit"]'
                ];

                const elements = Array.from(document.querySelectorAll(clickableSelectors.join(',')));

                return elements
                    .filter(el => {
                        const style = window.getComputedStyle(el);
                        const rect = el.getBoundingClientRect();
                        return (
                            style.pointerEvents !== 'none' &&
                            style.visibility !== 'hidden' &&
                            style.display !== 'none' &&
                            rect.width > 0 &&
                            rect.height > 0
                        );
                    })
                    .map(el => {
                        const rect = el.getBoundingClientRect();
                        return {
                            tag: el.tagName,
                            text: el.innerText.trim(),
                            x: rect.x,
                            y: rect.y,
                            width: rect.width,
                            height: rect.height
                        };
                    });
            }"""


    let clickable() =
        async {
            let! page = page()
            let! clickableAreas = page.EvaluateAsync(clickableObjScript) |> Async.AwaitTask
            // Deserialize the result
            let resultsJson = clickableAreas.ToString()
            printfn "Clickable Elements:\n%A" resultsJson
            ()
        }

    let goBack() = async {
        let! page = page()
        let! _  = page.GoBackAsync() |> Async.AwaitTask
        ()
    }

    let goForward() = async {
        let! page = page()
        let! _  = page.GoForwardAsync() |> Async.AwaitTask
        ()
    }

    let typeText text = async {
        let! page = page()
        do! page.Keyboard.TypeAsync text |> Async.AwaitTask
    }

    let url () = async {
        let! page = page()
        return Some page.Url
    }

    let create() =
        let userInteraction =            
            {new IUserInteraction with
                member _.doubleClick(x,y) = doubleClick(x,y)
                member _.click(x,y,btn) = click(x,y,btn)
                member _.wheel(x,y) = wheel(x,y)
                member _.move(x,y) = move(x,y)
                member _.scroll (x,y) (scrollX,scrollY) = scroll (x,y) (scrollX,scrollY)
                member _.pressKeys keys = pressKeys keys
                member _.dragDrop (sX,sY) (tX,tY) = dragDrop (sX,sY) (tX,tY)
                member _.snapshot() = snapshot()
                member _.goBack () = goBack()
                member _.goForward () = goForward()
                member _.typeText text = typeText text
                member _.url () = url()
            }
        Pw {|postUrl=postUrl; driver=userInteraction|}