module PortingPlan
open System
open FsOpCore
open System.Text.Json
open Microsoft.SemanticKernel

let t_Login =
    { OTask.Create() with
        id = "login"
        description = "Get past the login page"
        target = OLink "https://att.com"
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = None //use default
        cua = Some $"""the user wants to 'port out pin' from att.com
Find the location on the att.com site where that is possible.
Try using the search option to find the required page.
You are looking for a page that show a clickable link or button for 'requesting a transfer pin'.
The user will need to login first. 
For login, use `{Environment.GetEnvironmentVariable("ATT_ID")}` as the login id and `{Environment.GetEnvironmentVariable("ATT")}` for password.
Dismiss any unneeded popups and notices.
If a web address cannot be reached, use the 'reload' and then 'home' tools to reset.
"""
        }

let t_FindPortOut =
    { OTask.Create() with
        id = "port out code"
        description = "Find the port out location"
        target = ONone
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = Some Prompts.``reasoner prompt for cua guidance``
        cua = Some $"""
"""
        }

let create() =
        let plan =
            { OPlan.Default with
                description = "Port out t-mobile number"
                root = ONode.Seq {nodes= [ONode.Leaf t_Login;]; description=None}
                //root = ONode.All {nodes= [ONode.One tSendEmails]; description=None}
            }
        plan


