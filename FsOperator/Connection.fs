namespace FsOperator
open System.Threading
open System.Threading.Tasks
open System.Net.Http
open System.Text.Json
open PuppeteerSharp

module Connection =

    let _connection : Ref<IBrowser option> = ref None
    let _toolsUrl : Ref<string option> = ref None
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
                    | true,e ->
                        let url = $"http://localhost:{C.DEBUG_PORT}{e.GetString()}"
                        _toolsUrl.Value <- Some url
                        debug url
                    | _ -> debug "devtoolsFrontendUrl not found"

                //debug $"http://localhost:{C.DEBUG_PORT}{devTools}"
                return wsUrl
            finally
                _manualRestEvent.Reset() |> ignore
        }

    let runInitScript(page: IPage) =
        task {
            try
                do! page.WaitForNetworkIdleAsync()
                // Execute JavaScript in the new page
                let! _ = page.EvaluateExpressionAsync("console.log('New page script executed!')") |> Async.AwaitTask

                // Optional: remove target="_blank"
                let script = """
                () => {
                    document.querySelectorAll('a[target="_blank"]')
                            .forEach(a => a.removeAttribute('target'));
                }
                """

                let! _ = page.EvaluateExpressionAsync("""
                    window.open = (url) => {
                        window.location.href = url;
                    };
                """)

                let! _ = page.EvaluateFunctionAsync(script)
                debug "New page script executed!"
                return ()
            with ex ->
                // Handle any exceptions that occur during script execution
                debug $"Error executing script in new page: {ex.Message}"
                return ()
        }

    let logNewRequest (newPage: IPage) =
        newPage.Request.Add(fun  e ->
            let url = e.Request.Url
            let method = e.Request.Method
            let headers = e.Request.Headers
            let postData = e.Request.PostData
            debug $"Request: {method} {url}"
            debug $"Headers: {headers}"
            debug $"Post Data: {postData}"
        )

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
            let! pages = browser.PagesAsync() |> Async.AwaitTask
            browser.add_TargetCreated(fun sender e ->
                task {
                    if e.Target.Type = TargetType.Page then
                        let! newPage = e.Target.PageAsync()
                        do! runInitScript newPage
                        do logNewRequest newPage
                }
                |> ignore
            )
            for p in pages do
                do! runInitScript p
            return browser
        }


    let shutdown() =
        async {
            match _connection.Value with
            | Some conn ->
                conn.Disconnect()
                do! conn.CloseAsync() |> Async.AwaitTask
                _connection.Value <- None
            | None -> ()
        }

    let closeConnection() =
        async {
            match _connection.Value with
            | Some conn ->  conn.Disconnect()
            | None -> ()
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


    let connection () =
        async {
            let! w = Async.AwaitWaitHandle(_manualRestEvent,2000)
            match _connection.Value with
            | None -> return! reconnect 2
            | Some conn when conn.IsClosed || not conn.IsConnected -> return! reconnect 2
            | Some conn -> return conn
        }

