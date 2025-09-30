namespace TMOnboad.Client
open System
open Bolero

//Very often need multiple 'pages' even in a single page app (eg. for authentication).
//Here we are retaining the paging mechanism, even though there is only one page in this app
type Page =
    | [<EndPoint "/">] Home
    //| [<EndPoint "/authentication/{action}">] Authentication of action:string 

//Application model or state for the UI
type Model = {
    count   : int
    isDarkMode : bool
    page    : Page
    credentialsRequested : bool
    codeRequested : bool
    email : string
    password : string
    code : string
    image : string option
    action : string 
}

//Type definition for data exchanged between client and server in a message (as JSON over the wire)
type ClientInfo = {
    Time: DateTime
}

///Messages sent by the server to the client
type ServerInitiatedMessages =
    | Srv_Notification of string
    | Srv_ScreenShot of string
    | Srv_Action of string
    | Srv_SendCreds
    | Srv_SendCode
    | Srv_Usage of (int*float)
    | Srv_DonePlan

///Messages sent by the client to the server
type ClientInitiatedMessages =
    | Clnt_Reset of string      //need message parameter for signalR serialization (it seems)
    | Clnt_Connected of ClientInfo  //
    | Clnt_StartFlow of string
    | Clnt_Credentials of string * string
    | Client_Code of string
    
///Elmish messages handled by the update function
type Message =
    | Reset
    | Nop of unit 
    | Error of exn
    | SetPage of Page
    | StartFlow
    | Started 
    | SendCredentials of string * string
    | SendCodes of string
    | FromServer of ServerInitiatedMessages
    | ToggleDarkMode
