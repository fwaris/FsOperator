namespace TMOnboad.Client.Views
open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Web
open Elmish
open Bolero
open Bolero.Html
open TMOnboad.Client
open MudBlazor

type MainLayout() =
    inherit ElmishComponent<Model,Message>()    

    override this.View model dispatch =        
        match model.page with 
        | Page.Home -> 
            concat {
                comp<PageTitle> { text "TM Onboard" }
                comp<MudThemeProvider> {"IsDarkMode" => model.isDarkMode}
                comp<MudDialogProvider> {attr.empty()}
                comp<MudSnackbarProvider> {attr.empty()}                
                comp<MudLayout> {
                    AppBar.appBar model dispatch
                    comp<MudMainContent> {
                        comp<MudPaper> {
                            "Class" => "pa-4 ma-4"
                            "Style" => "margin-top:20px;"
                            "Justify" => Justify.Center                            
                            img {
                                attr.id "screenshotCanvas"
                                attr.style "width:800px; height:auto; border:1px solid #d3d3d3;"
                                attr.src (match model.image with Some img -> img | None -> "")
                            }
                        }
                        comp<MudGrid> {
                            "Justify" => Justify.Center
                            comp<MudItem> {
                                "xs" => 4
                                comp<MudSpacer> {attr.empty()}
                            }
                            comp<MudItem> {
                                "xs" => 8
                                comp<MudPaper> {
                                    "Class" => "pa-4 ma-4"
                                    "Justify" => Justify.Center
                                    "Style" => "max-width:400px;" 
                                    comp<MudForm> {
                                        attr.disabled (not model.credentialsRequested)
                                        comp<MudTextField<string>> {
                                            "Label" => "Email"
                                            "Variant" => Variant.Outlined
                                            "Value" => model.email
                                        }
                                        comp<MudTextField<string>> {
                                            "Label" => "Password"
                                            "Variant" => Variant.Outlined
                                            "InputType" => InputType.Password
                                            "Value" => model.password
                                        }
                                    }
                                    div {
                                        attr.``class`` "d-flex"
                                        comp<MudButton> {
                                            attr.disabled (not model.credentialsRequested)
                                            on.click (fun _ -> dispatch (SendCredentials (model.email, model.password)))
                                            text "Submit Credentials"
                                        }
                                    }
                                }                                
                            }
                            comp<MudItem> {
                                "xs" => 4
                                comp<MudSpacer> {attr.empty()}
                            }
                        }
                        comp<MudPaper> {
                            "Justify" => Justify.Center
                            comp<MudGrid> {
                                comp<MudItem> {
                                    "xs" => 12
                                    comp<MudPaper> {
                                        "Class" => "pa-4 ma-4"
                                        "Style" => "max-width:400px;"
                                        comp<MudForm> {
                                            attr.disabled (not model.codeRequested)
                                            comp<MudTextField<string>> {
                                                "Label" => "Code"
                                                "Variant" => Variant.Outlined
                                                "Value" => model.code
                                            }
                                        }
                                        div {
                                            attr.``class`` "d-flex"
                                            comp<MudButton> {                                
                                                attr.disabled (not model.codeRequested)
                                                on.click (fun _ -> dispatch (SendCodes model.code))
                                                text "Submit Code"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
