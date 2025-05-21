namespace FsOperator
open FsOpCore
open Microsoft.Playwright
open SkiaSharp
open System.Threading

module Browser =
    let _connection : Ref<IBrowser option> = ref None
    let _waitHandle : Ref<ManualResetEvent option> = ref None
    
    let launch (handle:WaitHandle) (launchHandle:ManualResetEvent) = 
        async {
            try
                use! playwright = Playwright.CreateAsync() |> Async.AwaitTask
                let browserOptions = BrowserTypeLaunchOptions(Headless = false)
                let! browser = playwright.Chromium.LaunchAsync(browserOptions) |> Async.AwaitTask                       
                //let contextOptions = BrowserNewContextOptions()
                //contextOptions.ViewportSize <- ViewportSize(Width=1280, Height=768)
                //let! context = browser.NewContextAsync(contextOptions) |> Async.AwaitTask
                let! page = browser.NewPageAsync() |> Async.AwaitTask
                do! page.SetViewportSizeAsync(1280, 768) |> Async.AwaitTask
                page.SetDefaultTimeout(10000f)
                //let! r =  context.Pages.[0].GotoAsync("https://www.google.com") |> Async.AwaitTask
                _connection.Value <- Some browser
                launchHandle.Set() |> ignore
                let! r = Async.AwaitWaitHandle handle
                ()
            with ex -> 
                Log.exn (ex,"Error in launch")
                return raise ex
        }


    let connection () = 
        async {
            match _connection.Value with
            | Some conn when conn.IsConnected -> return conn
            | _ -> 
                _waitHandle.Value <- Some (new ManualResetEvent(false))           
                use whLaunch = new ManualResetEvent(false)
                do Async.Start(launch _waitHandle.Value.Value whLaunch)
                let! r = Async.AwaitWaitHandle(whLaunch, 5000)
                if r then 
                    Log.info "browser launched"
                    return _connection.Value.Value
                else 
                    Log.info "browser launch failed"
                    return failwith "browser launch failed"
        }
     
    let page () = 
        async {            
            let! browser = connection() 
            Log.info $"Browser connected: {browser.IsConnected}"
            let page = browser.Contexts.[0].Pages |> Seq.last
            Log.info "got pages; waiting for network idle ..."
            let loadState = LoadState.NetworkIdle
            let opts = PageWaitForLoadStateOptions()
            opts.Timeout <- 1000.f
            do! page.WaitForLoadStateAsync(loadState,options=opts) |> Async.AwaitTask            
            Log.info "wait for idle completed"
            //do! page.BringToFrontAsync() |> Async.AwaitTask
            //for f in page.Frames do 
            //    debug $"frame: {f.Url} isMain {page.MainFrame.Url = f.Url}"
            //debug "----"
            //let opts = WaitForNetworkIdleOptions()
            //opts.Timeout <- 1000
            //opts.IdleTime <- 200
            //do! page.WaitForNetworkIdleAsync() |> Async.AwaitTask           
            return page
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

    let scroll(x:int,y:int) = async{
        let! page = page()
        let parms = [|x :> obj; y|]
        let! _ = page.EvaluateAsync("function(x, y) { window.scrollBy(x, y); }",parms) |> Async.AwaitTask
        ()
    }

    let pressKeys (keys:string list) = 
        async {
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
            let! image = page.ScreenshotAsync() |> Async.AwaitTask
            Log.info $"done snapshot"
            let bmp = SKBitmap.Decode(image)
            let imgUrl = FsResponses.RUtils.toImageUri image
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(homePath.Value, @"screenshot.png"), image)
            return imgUrl,(int bmp.Width, int bmp.Height)
        }


    let shutdown() = 
        async {
            try
                match _waitHandle.Value with  Some w -> w.Set() | None -> ()
                match _connection.Value with Some conn -> conn.CloseAsync() |> ignore| None -> ()                
            with ex -> 
                Log.exn ( ex,"Error in shutdown")
                _connection.Value <- None
        }

    let launchExternal() = 
            async {
            let! browser = connection() 
            let r = {|pid = 0|}
            return r
        }