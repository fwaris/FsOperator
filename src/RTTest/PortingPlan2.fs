module PortingPlan2
open System
open System.IO
open FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.Text.Json

//environment variable containing credentials
let ID = "PORT_OUT_ID_2"
let PW = "PORT_OUT_PW_2"
let URL = "PORT_OUT_URL_2"

module MCP = 
    open ModelContextProtocol.Client
    open System.Net.Http
    let sendBill (fileName:string, fileBytes:byte[]) = task {
        //transport and client
        let sseOptions = SseClientTransportOptions(Endpoint = Uri("http://localhost:5000/sse"),Name = "FSharpClient")
        use httpClient = new HttpClient()
        let transport = SseClientTransport(sseOptions, httpClient, ownsHttpClient = false)
        let! client = McpClientFactory.CreateAsync(transport)

        //tool call
        let parms = readOnlyDict [
            "fileName", fileName :> obj
            "fileBytes",   fileBytes
        ]    
        let! result = client.CallToolAsync("ReceiveBill", parms)

        // Extract and print the text content from the result
        let textContent = result.Content |> Seq.tryFind (fun c -> c.Type = "text")
        match textContent with
        | Some content -> printfn "Tool response: %s" content.Text
        | None -> printfn "No text content received."
    }

module FileOpener =
    open System
    open System.Diagnostics
    open System.Runtime.InteropServices
    open System.IO

    /// Opens the given path with the OS default app (macOS/Windows/Linux).
    let openWithDefaultApp (path: string) =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "Path is required."

        let fullPath = Path.GetFullPath path
        let psi = ProcessStartInfo()

        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            psi.FileName <- "open"
            psi.ArgumentList.Add fullPath
            psi.UseShellExecute <- false
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            // On Windows you pass the file directly and enable ShellExecute
            psi.FileName <- fullPath
            psi.UseShellExecute <- true
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
            psi.FileName <- "xdg-open"
            psi.ArgumentList.Add fullPath
            psi.UseShellExecute <- false
        else
            raise (PlatformNotSupportedException())

        Process.Start psi |> ignore

type RecurringCharge = {
    desc : string
    amount : decimal
}

type Bill = {
    phone : string
    total : decimal
    recurringCharges : RecurringCharge list
}

///SK plugin for bill and pin related functions
type BillFunctions() = 

    [<KernelFunction("save_pin")>]
    [<Description("Save the port out pin with the phone number")>]
    member this.save_pin(phone:string, pin:string) = 
        let comp = async {
            try
                let f = homePath.Value @@ "port_pin.txt"
                File.WriteAllText(f, $"{phone}:{pin}")
                return "pin saved"
            with ex ->
                Log.exn(ex, nameof this.save_pin)
                return $"error occured while trying to save pin"
        }
        Async.StartAsTask comp

    //[<KernelFunction("save_bill")>]
    [<Description("Save the bill details")>]
    member this.save_bill(bill: Bill) = 
        let comp = async {
            try
                let f = homePath.Value @@ "bill_details.txt"
                let json = JsonSerializer.Serialize(bill,Utility.openAIResponseSerOpts)
                File.WriteAllText(f, json)
                return "bill details saved"
            with ex ->
                Log.exn(ex, nameof this.save_bill)
                return $"error occured while trying to save bill details"
        }
        Async.StartAsTask comp

    [<KernelFunction("bill_downloaded")>]
    [<Description("Notify that the bill has been downloaded")>]
    member this.bill_downloaded() = 
        let comp = async {
            try
                let dir = PlaywrightDriver.downloadsPath.Value
                Directory.GetFiles(dir)
                |> Seq.map(FileInfo)
                |> Seq.map (fun x -> printfn $"{x.Name}, {x.LastAccessTimeUtc}, {x.LastWriteTimeUtc}"; x)
                |> Seq.tryHead
                |> Option.map(fun f -> 
                    let timestamp = f.LastAccessTime.ToString("yyyy-MM-dd_HH-mm-ss");
                    let f2 = PlaywrightDriver.downloadsPath.Value @@ $"bill_{timestamp}.pdf"
                    File.Copy(f.FullName, f2)
                    Log.info $"Bill downloaded: {f2}"
                    FileOpener.openWithDefaultApp(f2)
                    MCP.sendBill(f2, File.ReadAllBytes(f2)) |> ignore)
                |> Option.orElseWith (fun _ -> Log.info "No recent bill downloaded"; None)
                |> ignore
                return "bill downloaded"
            with ex ->
                Log.exn(ex, nameof this.save_bill)
                return $"error occured while trying to save bill details"
        }
        Async.StartAsTask comp

let t_Login =
    { OTask.Create() with
        id = "login"
        description = "Get past the login page"
        target = OLink $"""{System.Environment.GetEnvironmentVariable(URL)}"""
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = None //use default
        cua = Some $"""Log in to {System.Environment.GetEnvironmentVariable(URL)}.
For login, use `{Environment.GetEnvironmentVariable(ID)}` as the login id and `{Environment.GetEnvironmentVariable(PW)}` for password.
Note: if you see the password is already entered, don't enter it again.
Dismiss any unneeded popups and notices.
The task ends when the user is logged in and the account page for the user is showing.
Check the 'remember me' type box, if you see it in the login pages.
If prompted, choose password based login.
"""
        }

let t_FindPortOut =
    { OTask.Create() with
        id = "port out code"
        description = "Obtain the port out pin"
        target = ONone
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = Some Prompts.``reasoner prompt for cua guidance``
        cua = Some $"""the user wants to 'port out pin' from the telecom provider.
Go to 'Profile' and then 'Settings'
Find a clickable link or button for 'requesting a transfer pin'.
If asked, choose the phone number ending in '08' for which the pin is requested.
Note down the pin. Save it to memory and invoke the 'save_pin' tool to save the pin along with the phone number.
"""
        }

let t_GetBillDetails =
    { OTask.Create() with
        id = "download bill"
        description = "Download the bill"
        target = ONone
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                    typeof<BillFunctions>
                ]
        reasoner = Some Prompts.``reasoner prompt for cua guidance``
        cua = Some $"""Your task is to download the bill pdf.
- Under 'Billing', look for 'Download PDF' to download the bill. 
-- The main link it usually at the top.
- * If you see the pdf of the bill rendered on the screen, use the `download_current_page_pdf` tool to download it. *
--  Note that there is no visual indication on the page for the download, once the download is started.
- If you think the download has started, invoke the 'bill_downloaded' tool.
- If the tool response is NOT affirmative, call the tool a few more times to ensure the bill is downloaded.
- Then end the task.
"""
    }

let create() =
        Directory.GetFiles(PlaywrightDriver.downloadsPath.Value) |> Seq.iter (fun f -> File.Delete(f))
        let plan =
            { OPlan.Default with
                description = "Port out t-mobile number"
                //root = ONode.Seq {nodes= [ONode.Leaf t_Login; ONode.Leaf t_FindPortOut; ONode.Leaf t_GetBillDetails]; description=None}
                root = ONode.Seq {nodes= [ONode.Leaf t_Login; ONode.Leaf t_GetBillDetails]; description=None}
                //root = ONode.All {nodes= [ONode.One tSendEmails]; description=None}
            }
        plan


let kernel() = OPlan.defaultKernel Map.empty  (Some(fun b-> b.Plugins.AddFromType<BillFunctions>()|>ignore))

let createWithKernel() = 
    create(), kernel()