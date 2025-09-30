namespace TMOnboard.Flow
open System
open FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.IO
open System.Text.Json

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
    let bus : Ref<WBus<TaskFlowMsgIn,TaskFlowMsgOut>> = Unchecked.defaultof<_>

    member this.SetBus(b:WBus<TaskFlowMsgIn,TaskFlowMsgOut>) = bus.Value <- b

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
                    McpClient.MCP.sendBill(f2, File.ReadAllBytes(f2)) |> ignore)
                |> Option.orElseWith (fun _ -> Log.info "No recent bill downloaded"; None)
                |> ignore
                return "bill downloaded"
            with ex ->
                Log.exn(ex, nameof this.save_bill)
                return $"error occured while trying to save bill details"
        }
        Async.StartAsTask comp


    [<KernelFunction("get_credentials")>]
    [<Description("Ask the user to supply the credentials user name/email and/or password")>]
    member this.get_credentials() = 
        let comp = async {
            try
                if bus.Value <> Unchecked.defaultof<_> then
                    bus.Value.PostToFlow(W_Msg RSNRi_GetCredentials)
                    return "credentials requested"
                else
                    Log.warn "No bus available to request credentials"
                    return "internal error: unable to request credentials"

            with ex ->
                Log.exn(ex, nameof this.save_bill)
                return $"error occured while trying post get credentials message"
        }
        Async.StartAsTask comp


    [<KernelFunction("get_code")>]
    [<Description("Ask the user to supply MFA code for further authentication")>]
    member this.get_code() = 
        let comp = async {
            try
                if bus.Value <> Unchecked.defaultof<_> then
                    bus.Value.PostToFlow(W_Msg RSNRi_GetCode)
                    return "code requested"
                else
                    Log.warn "No bus available to request code"
                    return "internal error: unable to request code"

            with ex ->
                Log.exn(ex, nameof this.save_bill)
                return $"error occured while trying post get code message"
        }
        Async.StartAsTask comp
