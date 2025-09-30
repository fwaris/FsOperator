namespace TMOnboad.Client.Views
open System
open Bolero.Html
open MudBlazor
open Microsoft.AspNetCore.Components.Web

module AppBar =
    open TMOnboad.Client

    let appBar (model:Model) dispatch = 
        comp<MudAppBar> {
            "Fixed" => true
            "Dense" => true
            comp<MudGrid> {
                comp<MudItem> {
                    "xs" => 1
                    comp<MudButton> {
                        on.click (fun _ -> dispatch StartFlow)
                        text "Start"

                    }
                }
                comp<MudItem> {
                    "xs" => 10
                    comp<MudText> {
                        "Align" => Align.Center
                        "Typo" => Typo.h6
                        "TM Onboard"
                    }
                }
                comp<MudItem> {
                    "xs" => 1
                    div {
                        attr.``class`` "d-flex justify-end"
                        comp<MudIconButton> {
                            "Icon" => if model.isDarkMode then  Icons.Material.Filled.LightMode else Icons.Material.Filled.DarkMode
                            "Color" => Color.Inherit
                            on.click (fun _ -> dispatch ToggleDarkMode) 
                        }
                    }
                }
            }
        }
