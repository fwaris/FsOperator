namespace FsOperator
open FsOpCore
open Avalonia.FuncUI.Hosts
open Avalonia.Threading

module Plans =
    
    let editPlan (win:HostWindow,model:Model)  =
        task {
            return!
                Dispatcher.UIThread.InvokeAsync<OPlan option>(fun _ ->
                    task {
                        let plan = model.plan |> Option.defaultValue OPlan.Default
                        let dlg = FsOpPlanEditor.PlanEditor(plan)
                        return! dlg.ShowDialogAsync(win)
                    })
        }

