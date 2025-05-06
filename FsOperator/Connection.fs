namespace FsOperator
open System.Threading.Tasks
open System.Net.Http
open System.Text.Json
open PuppeteerSharp

module Connection =

    let _connection : Ref<IBrowser option> = ref None

    let getWebSocketDebuggerUrl (port: int) : Task<string> =
        task {
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
        }

    let connectToBrowser (port: int) : Task<IBrowser> =
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

    let rec private reconnect count =
        async {
            try 
                let! conn = connectToBrowser 9222 |> Async.AwaitTask
                _connection.Value <- Some conn
                return conn
            with ex -> 
                if count < 2 then 
                    do! closeConnection()
                    do! Async.Sleep(200) 
                    return! reconnect (count+1)
                else 
                    return failwith "Unable to connect to browser"

        }

    let connection () = 
        async {
            match _connection.Value with
            | None -> return! reconnect 2
            | Some conn when conn.IsClosed || not conn.IsConnected -> return! reconnect 2
            | Some conn -> return conn
        }

