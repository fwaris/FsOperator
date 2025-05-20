namespace FsOperator
open System
open System.Threading
open System.Threading.Tasks
open System.Net.Http
open System.Text.Json
open System.IO
open PuppeteerSharp
open FsOpCore
open SkiaSharp
open System.Threading.Channels
open System.Net.Sockets

type BST = BST_Init | BST_Ready | BST_AwaitAck
type BrowserAppState = {
    port        : int
    tokenSource : System.Threading.CancellationTokenSource
    outChannel  : Channel<P2PFromServer>
    pid         : int option
    listener    : TcpListener option
    state       : BST

}
with static member Create() =
            {
                port = P2p.defaultPort
                tokenSource = new System.Threading.CancellationTokenSource()
                outChannel = Channel.CreateBounded(10)
                pid = None
                listener = None
                state = BST_Init
            }


type BrowserMode = External of {|pid:int option|} | Embedded of BrowserAppState

module BrowserMode =
    let isEmbedded = function | Embedded _ -> true | _ -> false
    let isExternal = function | External _ -> true | _ -> false
    let pid = function | External p -> p.pid | Embedded b -> b.pid
    let port = function | External p -> P2p.defaultPort | Embedded b -> b.port
    let setPid pid = function | External p -> External {|pid = Some pid|} | Embedded b -> Embedded {b with pid= Some pid}
    let setEmbState state = function | External p -> External p | Embedded b -> Embedded {b with state=state}
    let setEmbAppState bst = function | External p -> External p | Embedded b -> Embedded bst


    let isReady browserMode =
        match browserMode with
        | External p -> p.pid.IsSome
        | Embedded b -> b.state = BST_Ready


module Browser =

    let _connection : Ref<IBrowser option> = ref None
    let _manualRestEvent = new ManualResetEvent(false)

    let getWebSocketDebuggerUrl (port: int) : Task<string> =
        task {
            try 
                _manualRestEvent.Set() |> ignore
                use client = new HttpClient()
                let! response = client.GetStringAsync(sprintf "http://localhost:%d/json/version" port)
                let json = System.Text.Json.JsonDocument.Parse(response)         
                let wsUrl = json.RootElement.GetProperty("webSocketDebuggerUrl").GetString()
                let! resp2 = client.GetStringAsync(sprintf "http://localhost:%d/json" port)
                let json2 = System.Text.Json.JsonDocument.Parse(resp2)           
                if json2.RootElement.ValueKind = JsonValueKind.Array && json2.RootElement.EnumerateArray() |> Seq.tryHead |> Option.isSome then                 
                    let j0 = json2.RootElement.Item(0)
                    match j0.TryGetProperty("devtoolsFrontendUrl") with
                    | true,e -> debug $"http://localhost:{C.DEBUG_PORT}{e.GetString()}"
                    | _ -> debug "devtoolsFrontendUrl not found"

                //debug $"http://localhost:{C.DEBUG_PORT}{devTools}"
                return wsUrl
            finally 
                _manualRestEvent.Reset() |> ignore
        }


    let private connectToBrowser (port: int) : Task<IBrowser> =        
        task {            
            let! wsUrl = getWebSocketDebuggerUrl C.DEBUG_PORT
            let options = ConnectOptions(BrowserWSEndpoint = wsUrl)
            options.DefaultViewport <- ViewPortOptions(Width = 1280, Height = 720)
            options.ProtocolTimeout <- 10000
            let! temp = Puppeteer.ConnectAsync(options)    
            let! _  = temp.CloseAsync() |> Async.AwaitTask //clear any hanging sessions
            let! browser = Puppeteer.ConnectAsync(options) |> Async.AwaitTask
            let! ua = browser.GetUserAgentAsync() |> Async.AwaitTask //this seems to prime the connection
            return browser
        }


    let shutdown() = 
        async {
            try
                match _connection.Value with
                | Some conn -> 
                    conn.Disconnect()
                    do! conn.CloseAsync() |> Async.AwaitTask
                    _connection.Value <- None
                | None -> ()
            with ex -> 
                Log.exn ( ex,"Error in shutdown")
                _connection.Value <- None
        }

    let closeConnection() =
        async {
            match _connection.Value with
            | Some conn ->  conn.Disconnect()                
            | None -> ()
        }

    let launchExternal ()  = 
        async {
            let! dnldRstl  = (new BrowserFetcher()).DownloadAsync(BrowserTag.Stable)  |> Async.AwaitTask
            dnldRstl.BuildId |> printfn "Chromium downloaded: %s"
            let opts = LaunchOptions()
            opts.Headless <- false
            opts.Channel <- BrowserData.ChromeReleaseChannel.Stable
            opts.DefaultViewport <- ViewPortOptions(Width = 1280, Height = 720)
            //opts.Args <-  
            //    [| 
            //        $"--remote-debugging-port={C.DEBUG_PORT}" 
            //        $"--remote-allow-origins=http://localhost:{C.DEBUG_PORT}"
            //    |]
            // Launch browser; PuppeteerSharp will handle Chromium download automatically
            let! browser = Puppeteer.LaunchAsync(opts) |> Async.AwaitTask
            _connection.Value <- Some browser
            return {| pid=browser.Process.Id|}
        }

    let reconnectExternal pid =
        let isRunning pid = 
            try
                let proc = System.Diagnostics.Process.GetProcessById(pid)
                proc.HasExited |> not
            with ex -> false
        async {
            match pid with 
            | Some pid when isRunning pid ->
                let opts = LaunchOptions()
                opts.Headless <- false
                opts.Channel <- BrowserData.ChromeReleaseChannel.Stable
                opts.DefaultViewport <- ViewPortOptions(Width = 1280, Height = 720)
                let! browser = Puppeteer.LaunchAsync(opts) |> Async.AwaitTask
                _connection.Value <- Some browser
                return {| pid=pid|}
            | None -> return! launchExternal()
        }

    let rec private _reconnect count =
        async {
            try 
                let! conn = connectToBrowser 9222 |> Async.AwaitTask
                _connection.Value <- Some conn
                return conn
            with ex -> 
                if count < 2 then 
                    do! closeConnection()
                    do! Async.Sleep(200) 
                    return! _reconnect (count+1)
                else 
                    return failwith "Unable to connect to browser"
        }        

    let private reconnect count =
        async {
            try
                _manualRestEvent.Set() |> ignore
                return! _reconnect 2
            finally
                _manualRestEvent.Reset() |> ignore
        }

(*
    let connection browserMode = 
        async {
            let! w = Async.AwaitWaitHandle(_manualRestEvent,2000) 
            match browserMode with
            | External p ->
                match p.pid with
                | Some pid when pid > 0 -> return! connectToBrowser C.DEBUG_PORT
                | _ -> return! launchExternal() |> Async.AwaitTask
            | Embedded b ->
                match _connection.Value with
                | None -> return! reconnect 2
                | Some conn when conn.IsClosed || not conn.IsConnected -> return! reconnect 2
                | Some conn -> return conn
        }
*)

    let connection () = 
        async {        
            match _connection.Value with
            | None -> return! reconnect 2
            | Some conn when conn.IsClosed || not conn.IsConnected -> return! reconnect 2
            | Some conn -> return conn
        }
     
    let page () = 
        async {
            let! browser = connection() 
            let! pages = browser.PagesAsync() |> Async.AwaitTask     
            let page = pages |> Seq.toList |> Seq.head
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

    let snapshot() = 
        async {                   
            let! page = page()
  
            let opts = ScreenshotOptions()
            opts.BurstMode <- true
            let! image = page.ScreenshotDataAsync() |> Async.AwaitTask
            do! page.SetBurstModeOffAsync() |> Async.AwaitTask
            let bmp = SKBitmap.Decode(image)
            let imgUrl = FsResponses.RUtils.toImageUri image
            File.WriteAllBytes(Path.Combine(homePath.Value, @"screenshot.png"), image)
            return imgUrl,(int bmp.Width, int bmp.Height)
        }

    let goToPage urls =  
           async {
            let! page = page()
            let! _ = page.GoToAsync(urls) |> Async.AwaitTask
            return ()
        }

    let postUrl url = function
        | External p -> goToPage url |> Async.Start
        | Embedded b -> b.outChannel.Writer.TryWrite (P2PFromServer.Server_SetUrl url ) |> ignore
        
    let dllPath = lazy(
        Environment.CurrentDirectory
        @@ ".." @@ ".." @@ ".." @@ ".."
        @@ "FsOpBrowser" @@ "bin" @@ "Debug" @@ "net9.0" @@ "FsOpBrowser.dll"
     )

    let launchBrowser (browserMode) fnExit =        
        let dllPath = System.IO.Path.GetFullPath(dllPath.Value)
        let si = System.Diagnostics.ProcessStartInfo() 
        si.FileName <- "dotnet"
        si.Arguments <- $""" "{dllPath}" {BrowserMode.port browserMode}"""
        let pd = new System.Diagnostics.Process()
        pd.StartInfo <- si
        pd.EnableRaisingEvents <- true
        pd.Exited.Add(fun _ -> 
            let msg = $"Browser process exited with code {pd.ExitCode}"
            Log.info msg           
            pd.Dispose()
            fnExit())
        pd.Start()