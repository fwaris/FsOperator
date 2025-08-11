module PortingPlan
open FsOpCore
open System.Text.Json
open Microsoft.SemanticKernel

let t_Login =
    { OTask.Create() with
        id = "login"
        description = "Get past the login page"
        target = OLink "https://t-mobile.com"
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = None //use default
        cua = Some $"""Login to t-mobile.com. Use FaisalWaris@yahoo.com as the login id. Wait for user to complete the rest of the login process.
If asked, select the 'active' account.
Dismiss any unneeded popups and notices.
The task ends when user is logged in.
"""
        }

let t_FindPortOut =
    { OTask.Create() with
        id = ""
        description = "Find the port out location"
        target = ONone
        tools = Toolbox.tools [
                    typeof<Functions.FsOpMemory>
                    typeof<Functions.FsOpNavigator>
                    typeof<Functions.FsOpTaskTools>
                ]
        reasoner = Some Prompts.``reasoner prompt for cua guidance``
        cua = Some $"""
The user wants to 'port out the number from t-mobile. 
Find the location on the t-mobile site where that is possible.
Dismiss any unneeded popups and notices.
The task ends when that page is reached.
"""
        }

let create() =
        let plan =
            { OPlan.Default with
                description = "Port out t-mobile number"
                root = ONode.Seq {nodes= [ONode.Leaf t_Login; ONode.Leaf t_FindPortOut]; description=None}
                //root = ONode.All {nodes= [ONode.One tSendEmails]; description=None}
            }
        plan


