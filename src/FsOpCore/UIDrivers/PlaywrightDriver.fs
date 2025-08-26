namespace FsOpCore
open System.IO
open Microsoft.Playwright
open SkiaSharp
open System.Threading

module PlaywrightDriver =
    open System.Net
    open System.Net.Http
    let downloadsPath =lazy(homePath.Value @@ "PrivateDownloads")
    let _connection : Ref<IBrowser option> = ref None
    let _waitHandle : Ref<ManualResetEvent option> = ref None
    let _prevUrl : Ref<string option> = ref None

    let edgePath() =
        let path =
            if isWindows() then
                Some @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
            elif isMac() then
                Some @"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            elif isLinux() then
                // common path for linux
                Some "/usr/bin/microsoft-edge"
            else
                None
        match path with
        
        | Some p when System.IO.File.Exists(p) -> Some p
        | _ -> None

    let browserStatePath = lazy(homePath.Value @@ "fsoperator.json")
    let getStorageStatePath = lazy(if File.Exists browserStatePath.Value then browserStatePath.Value else null)    

    let saveState (ctx:IBrowserContext) = async {
        Log.info "Saving browser context state"
        try
            let opts = BrowserContextStorageStateOptions()            
            opts.Path <- browserStatePath.Value
            do! ctx.StorageStateAsync(opts) |> Async.AwaitTask |> Async.Ignore
        with ex ->
            Log.warn $"Error encountered when saving browser context state ${ex.Message}"
    }


    let buildHttpClientFromPlaywrightCookies (cookies: BrowserContextCookiesResult seq) =
        let container = new CookieContainer()
        for c in cookies do
            try
                let cookie = Cookie(c.Name, c.Value, c.Path, c.Domain)
                container.Add(cookie)
            with ex ->
                Log.warn $"Error encountered when adding cookie ${c.Name}: {ex.Message}"
        let handler = new HttpClientHandler(UseCookies = true, CookieContainer = container)
        new HttpClient(handler)    

    //let disconnectHook (ctx:IBrowserContext) = 
    //    saveState ctx
    //    |> Async.Start

    let newPageHandler (page:IPage) = 
        task {
            do! page.SetViewportSizeAsync(C.VIEWPORT_WIDTH, C.VIEWPORT_HEIGHT)
            //page.Close.Add(fun p -> disconnectHook p.Context)
        }
        |> ignore

    let initContext(browser:IBrowser) = 
        async {
            let ctxOpts = BrowserNewContextOptions(StorageStatePath = getStorageStatePath.Value, AcceptDownloads = true)
            let! ctx = browser.NewContextAsync(ctxOpts) |> Async.AwaitTask            
            ctx.Page.Add(newPageHandler)
            let! page = ctx.NewPageAsync() |> Async.AwaitTask
            match _prevUrl.Value with 
            | Some url -> do! page.GotoAsync(url) |> Async.AwaitTask |> Async.Ignore 
            | None     -> ()
            do! page.SetViewportSizeAsync(C.VIEWPORT_WIDTH,C.VIEWPORT_HEIGHT) |> Async.AwaitTask 
            return page
        }

    let rec getPage count (browser:IBrowser) = 
        async{
            try
                let ctx = browser.Contexts |> Seq.tryFind (fun c -> c.Pages.Count > 0)
                match ctx with 
                | Some ctx -> 
                    let sortedPages =
                        ctx.Pages
                        |> Seq.toList
                        |> List.rev
                        |> List.sortByDescending (fun p -> p.ViewportSize.Width * p.ViewportSize.Height)
                    //sortedPages |> List.iter (fun p -> printfn $"{p.Url}")
                    let page = sortedPages.Head
                    if not (page.ViewportSize.Width = C.VIEWPORT_WIDTH && page.ViewportSize.Height = C.VIEWPORT_HEIGHT) then 
                        do! page.SetViewportSizeAsync(C.VIEWPORT_WIDTH,C.VIEWPORT_HEIGHT) |> Async.AwaitTask 
                    return page
                | None -> return! initContext browser
            with ex -> 
                Log.exn(ex,nameof getPage)
                if count < 2 then 
                    return! getPage (count+1) browser
                else
                    return raise ex
        }
        
    let launch (handle:WaitHandle) (launchHandle:ManualResetEvent) =
        async {
            try
                use! playwright = Playwright.CreateAsync() |> Async.AwaitTask
                let browserOptions = BrowserTypeLaunchOptions(                        
                        Headless = isLinux(),                        
                        DownloadsPath = downloadsPath.Value,
                       // Args = ["--disable-blink-features=AutomationControlled"; "--force-device-scale-factor=1"],
                        Args = ["--disable-blink-features=AutomationControlled"],
                        ExecutablePath = (edgePath() |> Option.defaultValue null))
                let! browser = playwright.Chromium.LaunchAsync(browserOptions) |> Async.AwaitTask                
                let! page = initContext browser
                page.SetDefaultTimeout(C.PLAYWRIGHT_DEFAULT_TIMEOUT)
                launchHandle.Set() |> ignore
                _connection.Value <- Some browser
                match _prevUrl.Value with
                | Some url -> do! page.GotoAsync(url) |> Async.AwaitTask |> Async.Ignore
                | None -> ()
                let! r = Async.AwaitWaitHandle handle //wait on launch thread else browser closes. (alt. approach use server mode)
                ()
            with ex ->
                Log.exn (ex,"Error in launch")
                return raise ex
        }

    let isProperUrl (url:string) =
        url.Trim().StartsWith("http", System.StringComparison.InvariantCultureIgnoreCase)    

    let rec private _connect () =
        async {
            match _connection.Value with
            | Some conn when conn.IsConnected -> return conn
            | _ ->
                let! c = Async.StartChild(shutdown(),1000)
                do! c
                _waitHandle.Value <- Some (new ManualResetEvent(false))
                use whLaunch = new ManualResetEvent(false)
                do Async.Start(launch _waitHandle.Value.Value whLaunch)
                let! r = Async.AwaitWaitHandle(whLaunch, int C.PLAYWRIGHT_DEFAULT_TIMEOUT)
                if r then
                    Log.info "browser launched"
                    return _connection.Value.Value
                else
                    Log.info "browser launch failed"
                    return failwith "browser launch failed"
        }

    ///serialize connection requests due possible race conditions
    and _connectionAgent = MailboxProcessor.Start(fun inbox -> async {
        while true do 
            let! (rc:AsyncReplyChannel<IBrowser>) = inbox.Receive()
            let! browser = _connect()
            rc.Reply(browser)
    })

    and connection() = async {
        let! browser = _connectionAgent.PostAndAsyncReply(fun rc -> rc)
        return browser
    }

    and page () =
        async {
            let! browser = connection()
            let! page = getPage 0 browser
            Log.trace "got page; waiting for network idle ..."
            let! c = Async.StartChild(waitForIdle page, 1500)
            try do! c with ex -> Log.info $"waitForIdle failed"
            if isProperUrl page.Url then
                _prevUrl.Value <- Some page.Url
            return page
        }

    and shutdown() =
        async {
            try
                try
                    match _connection.Value with 
                    | Some conn -> 
                        let! page = page()
                        saveState page.Context |> Async.Start
                        do! Async.Sleep 1000
                        conn.CloseAsync() |> ignore
                    | None -> ()
                    match _waitHandle.Value with  Some w -> w.Set() |> ignore | None -> ()
                with ex ->
                    Log.exn ( ex,"Error in shutdown")
            finally
                _waitHandle.Value <- None
                _connection.Value <- None
        }

    and waitForIdle (page:IPage) =
        async {
            //let loadState = LoadState.NetworkIdle
            let loadState = LoadState.DOMContentLoaded
            let opts = PageWaitForLoadStateOptions()
            opts.Timeout <- 1000.f
            do! page.WaitForLoadStateAsync(loadState,options=opts) |> Async.AwaitTask
        }


    let pageDown() = async {
        let! page = page()
        let! _ =  page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)") |> Async.AwaitTask
        return ()
    }


    let click(x:int,y:int, btn:FsOpCore.MouseButton) = async{
        let! page = page()
        let btn =
            match btn with
            | FsOpCore.MouseButton.Left -> MouseButton.Left
            | FsOpCore.MouseButton.Middle -> MouseButton.Middle
            | FsOpCore.MouseButton.Right -> MouseButton.Right
        let opts = MouseClickOptions(Button = btn)
        do! page.Mouse.ClickAsync(float32 x, float32 y, opts) |> Async.AwaitTask
    }

    let doubleClick(x:int,y:int) = async{
        let! page = page()
        do! page.Mouse.DblClickAsync(float32 x, float32 y) |> Async.AwaitTask
    }

    let wheel(deltaX:int,y:int) = async{
        let! page = page()
        do! page.Mouse.WheelAsync(float32 deltaX, float32 y) |> Async.AwaitTask
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
            let keys = DriverUtils.canonicalize keys
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
        let rec loop count = 
            async {
                try
                    let! page = page()
                    Log.trace $"taking snapshot of {page.Url}"
                    let opts = PageScreenshotOptions()
                    opts.Animations <- ScreenshotAnimations.Disabled
                    opts.FullPage <- true
                    let! image = page.ScreenshotAsync() |> Async.AwaitTask
                    Log.trace $"done snapshot"
                    let bmp = SKBitmap.Decode(image)
                    let imgUrl = FsResponses.RUtils.toImageUri image
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(homePath.Value, @"screenshot.png"), image)
                    return imgUrl,(bmp.Width, bmp.Height)
                with ex ->
                    Log.exn(ex, "snapshot")
                    if count < 2 then 
                        return! loop (count + 1)
                    else
                        return raise ex
            }
        loop 0


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

    let goToPage (url:string) = async {
        let! p = page()
        let! r = p.GotoAsync(url) |> Async.AwaitTask
        if not r.Ok then 
            Log.warn $"unable to load page {url}"
        let! p' = page() //wait for new page to settle
        return ()
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
            {new IUIDriver with
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
                member _.environment with get (): string = FsResponses.ComputerEnvironment.browser
                member _.start (arg: string) = if isEmpty arg then async{return()} else goToPage arg
                member _.saveState() = async {
                    try 
                        let! page = page()
                        do! saveState page.Context
                    with ex ->
                        Log.exn(ex,"saveState")

                }
                member _.clearCookies() = async {
                    try 
                        let! page = page()
                        do! page.Context.ClearCookiesAsync() |> Async.AwaitTask
                    with ex ->
                        Log.exn(ex,"saveState")
                }
                member _.reload() = async {
                    try 
                        let! page = page()
                        let! r = page.ReloadAsync() |> Async.AwaitTask
                        return ()
                    with ex ->
                        Log.exn(ex,"saveState")
                }
                member _.getUrlBytes() = async {
                    try 
                        let! page = page()
                        let! cookies = page.Context.CookiesAsync() |> Async.AwaitTask

                        // build HttpClient with same cookies
                        let client = buildHttpClientFromPlaywrightCookies cookies  
                        let pdfUrl = page.Url
                        let! pdfBytes = client.GetByteArrayAsync(pdfUrl) |> Async.AwaitTask
                        return pdfBytes
                    with ex ->
                        Log.exn(ex,"getUrlBytes")
                        return Array.empty<byte>
                }
            }
        Pw {|postUrl=postUrl; driver=userInteraction|}
