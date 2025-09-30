module PortPinV
open System
open System.IO
open FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.Text.Json
open TMOnboard.Flow

//environment variable containing credentials
let ID = "PORT_OUT_ID_2"
let PW = "PORT_OUT_PW_2"
let URL = "PORT_OUT_URL_2"

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