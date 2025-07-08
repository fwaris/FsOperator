namespace FsOpPlanEditor
open System
open FsOpCore
open Elmish
open Avalonia.FuncUI.Elmish
open Avalonia.Controls
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.FuncUI.Hosts
open System.Threading.Tasks

[<AbstractClass; Sealed>]
type MainView =    
    static member tasks (model:Model) dispatch = 
        let btns = model.tasks |> List.map (fun t -> Button.create [Button.content t.id; Button.onClick(fun _ -> dispatch (EditTask) )] :> IView) 
        Panel.create [
            Grid.row 1
            Panel.children btns
        ]

    static member toolbar (model:Model) dispatch = 
        DockPanel.create [
            Grid.row 0
            DockPanel.children [                
                Button.create [Button.content "Add"; DockPanel.dock Dock.Left; Button.onClick (fun _ -> dispatch AddTask)]
                Button.create [Button.content "Cancel"; DockPanel.dock Dock.Right; Button.onClick (fun _ -> dispatch Close)]
                Button.create [Button.content "Save"; DockPanel.dock Dock.Right; Button.onClick (fun _ -> dispatch Save)]
            ]
        ]

    static member main (model:Model) dispatch =
        DockPanel.create [               
            DockPanel.children [
                Grid.create [
                    Grid.rowDefinitions "50,*"                   
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.clipToBounds true
                    Grid.children [
                        MainView.toolbar model dispatch  
                        MainView.tasks model dispatch
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
