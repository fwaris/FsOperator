namespace TMOnboad.Client
open System
open System.Threading.Tasks
open Elmish
open MudBlazor

type UpdateParms = 
    {
        snkbar              : ISnackbar
        serverDispatch      : ClientInitiatedMessages -> Task        
    }

module Update =
        
    let initModel =    
        {
            count = 0
            isDarkMode = true
            page = Home
            credentialsRequested = false
            codeRequested = false
            email = ""
            password = ""
            code = ""
            image = None
            action = ""
        }

    let notify (snkbar:ISnackbar) (msg:string) = snkbar.Add msg |> ignore
    let notifyError (snkbar:ISnackbar) (msg:string) =  snkbar.Add(msg,severity = Severity.Error) |> ignore

    // wrapper for sending messages to the server that meets the type signature of the Elmish Cmd.ofTask
    let send (serverDispatch:ClientInitiatedMessages -> Task) (msg:ClientInitiatedMessages) = 
        task{
            do! serverDispatch msg            
        }

    //Elmish update function
    let update (updParms:UpdateParms) (msg:Message) (model:Model) =
        match msg with
        | Started  -> model, Cmd.OfTask.either (send updParms.serverDispatch) (Clnt_Connected {Time=DateTime.Now}) Nop Error
        | Reset -> model,  Cmd.OfTask.either (send updParms.serverDispatch) (Clnt_Reset "") Nop Error
        | Error ex -> notifyError updParms.snkbar (ex.Message); model, Cmd.none
        | Nop _ -> model, Cmd.none
        | SetPage page -> {model with page = page}, Cmd.none
        | SendCredentials (email, password) -> 
            let newModel = {model with credentialsRequested = false; email=email; password=password}
            newModel, Cmd.OfTask.either (send updParms.serverDispatch) (Clnt_Credentials (email, password)) Nop Error
        | SendCodes code -> 
            let newModel = {model with codeRequested = false; code=code}
            newModel, Cmd.OfTask.either (send updParms.serverDispatch) (Client_Code code) Nop Error
        | ToggleDarkMode -> 
            let newModel = {model with isDarkMode = not model.isDarkMode}
            newModel, Cmd.none
        | StartFlow -> model, Cmd.OfTask.either (send updParms.serverDispatch) (Clnt_StartFlow "") Nop Error
        //from server
        | FromServer (Srv_Notification msg) -> notify updParms.snkbar msg; model, Cmd.none
        | FromServer (Srv_ScreenShot img) -> {model with image = Some img}, Cmd.none
        | FromServer (Srv_Action act) -> {model with action = act}, Cmd.none
        | x -> printfn $"{x}"; model,Cmd.none



 