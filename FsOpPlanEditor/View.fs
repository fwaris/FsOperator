namespace FsOpPlanEditor
open System
open FsOpCore
open Elmish
open Avalonia.FuncUI.Elmish
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks

[<AbstractClass; Sealed>]
type MainView =    
    static member main (model:Model) dispatch =
        DockPanel.create [               
            DockPanel.children [
                Grid.create [
                    Grid.rowDefinitions "50,*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        Button.create [
                            Button.content "Close"
                            Button.onClick (fun _ -> 
                                
                                dispatch Msg.Close)
                        ]
                    ]
                ]               
            ]
        ]
    
type PlanEditor(plan:OPlan) as this =
    inherit HostWindow()
    let tcs = new TaskCompletionSource<OPlan option>()

    do
        base.Title <- "Plan Editor"
        base.Width <- 400.0
        base.Height <- 600.0

        Program.mkProgram Update.init (Update.update this tcs) MainView.main
        |> Program.withHost this
        //|> Program.withConsoleTrace        
        |> Program.runWithAvaloniaSyncDispatch (plan)


    member this.ShowDialogAsync(parent: Window) : Task<OPlan option> =
        base.ShowDialog(parent) |> ignore
        tcs.Task
