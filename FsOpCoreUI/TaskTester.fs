namespace FsOpCoreUI
open Elmish
open System
open FsOpCore
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Elmish.ElmishHook
open Avalonia.Layout
open Avalonia.Media
open System.Threading.Tasks
open System.Threading.Channels
open Avalonia.FuncUI.Hosts

module TaskTester = 
    type MsgOut = Update of (ONode*ONode) //old update node
    
    module internal TaskTester =
        type Msg = 
            | StartTest 
            | StopTest 
            | Error of string
            | Update of OTask
            | Close 
            | Save 
            | MsgFromRunner of TaskRunner.MsgOut

        type Model = 
            {
                    refNode          : ONode
                    task             : IWritable<OTask>
                    running          : IWritable<bool>
                    dispatchToRunner : Ref<TaskRunner.MsgIn -> unit>
            }


        let init (n:ONode,task:IWritable<OTask>,running:IWritable<bool>) ()=             
            {
                refNode = n
                task = task
                running = running
                dispatchToRunner = ref(fun _ -> ())
            }, 
            Cmd.none

        let update (win:HostWindow,tcs:TaskCompletionSource<(ONode*ONode) option>) msg model = 
            match msg with 
            | StopTest -> model.dispatchToRunner.Value TaskRunner.Stop; model,Cmd.none
            | StartTest -> model.dispatchToRunner.Value TaskRunner.Start; model,Cmd.none
            | Update t -> model.task.Set(t); model, Cmd.none
            | Close -> tcs.SetResult(None);win.Close(); model,Cmd.none
            | Save -> tcs.SetResult(Some(model.refNode,ONode.Leaf model.task.Current));win.Close(); model,Cmd.none
            | Error e -> Log.info e; model, Cmd.none
            | MsgFromRunner (TaskRunner.Error e) -> model,Cmd.ofMsg (Error e)

        let taskEdit model dispatch = 
            let cache : Ref<TextBox> list = 
                [for _ in 1 .. ((FSharp.Reflection.FSharpType.GetRecordFields typeof<OTask>).Length - 1) -> //textboxes for all fields except id  
                    (ref Unchecked.defaultof<_>)]
            let task = model.task.Current
            Grid.create [
                Grid.rowDefinitions "30,30,150,*,30"
                Grid.columnDefinitions "100,*"
                Grid.maxHeight 700.
                Grid.children [                
                    TextBlock.create [
                        Grid.row 0
                        Grid.column 0
                        TextBlock.text "Id"
                        Control.margin 2
                        Control.verticalAlignment VerticalAlignment.Center
                        Control.horizontalAlignment HorizontalAlignment.Right
                    ]
                    TextBox.create [
                        TextBox.init (fun x -> cache.[0].Value <- x)
                        Grid.row 0
                        Grid.column 1
                        TextBox.text (string task.id)
                        Control.margin 2
                    ]
                    TextBlock.create [
                        Grid.row 1
                        Grid.column 0
                        TextBlock.text "URL"
                        Control.verticalAlignment VerticalAlignment.Center
                        Control.horizontalAlignment HorizontalAlignment.Right
                        Control.margin 2
                    ]
                    TextBox.create [
                        TextBox.init (fun x -> cache.[1].Value <- x)
                        Grid.row 1
                        Grid.column 1
                        Control.margin 2
                        TextBox.text (task.target.TargetString())
                    ]
                    TextBlock.create [
                        Grid.row 2
                        Grid.column 0
                        TextBlock.text "Description"
                        Control.margin 2
                        Control.horizontalAlignment HorizontalAlignment.Right
                    ]
                    TextBox.create [
                        TextBox.init (fun x -> cache.[2].Value <- x)
                        Grid.row 2
                        Grid.column 1
                        TextBox.margin 3
                        TextBox.acceptsReturn true
                        TextBox.multiline true
                        TextBox.minHeight 60.
                        TextBox.text task.description
                    ]
                    TextBlock.create [
                        Grid.row 3
                        Grid.column 0
                        TextBlock.text "CUA Instructions"                    
                        Control.margin 2
                        Control.horizontalAlignment HorizontalAlignment.Right
                    ]
                    TextBox.create [
                        TextBox.init (fun x -> cache.[3].Value <- x)
                        Grid.row 3
                        Grid.column 1
                        TextBox.margin 3
                        TextBox.minHeight 150.
                        TextBox.text (task.cua |> Option.defaultValue "")
                        TextBox.watermark "Leave blank to use default voice asst. instructions"
                        TextBox.acceptsReturn true
                        TextBox.multiline true
                    ]
                    Button.create [
                        Grid.row 4
                        Grid.column 0
                        Button.verticalAlignment VerticalAlignment.Bottom
                        Button.horizontalAlignment HorizontalAlignment.Left
                        Grid.columnSpan 2
                        Control.margin 2
                        Button.content "Apply"
                        Button.onClick (fun _ -> 
                            let id = cache.[0].Value.Text
                            let target = cache.[1].Value.Text
                            let pTarget = OTaskTarget.parseTarget target
                            let description = cache.[2].Value.Text
                            let cua = cache.[3].Value.Text
                            let task' =
                                {OTask.Create() with 
                                    id = id
                                    target = pTarget
                                    description = description
                                    cua = cua |> checkEmpty
                                }
                            if task' <> task then  
                                model.task.Set(task')
                            )
                    ]
                ]
            ]
            :> IView

        let toolbar model dispatch = 
            DockPanel.create [
                DockPanel.margin 1.0
                Grid.row 0
                DockPanel.children [
                    StackPanel.create [
                        DockPanel.dock Dock.Left;
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.children [
                            Button.create [Button.content Icons.cancel; Button.onClick (fun _ -> dispatch Close)]
                            Button.create [Button.content Icons.accept; Button.onClick (fun _ -> dispatch Save)]
                        ]
                    ]
                    StackPanel.create [
                        DockPanel.dock Dock.Left;
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.children [
                            Button.create [Button.content Icons.start; Button.onClick (fun _ -> dispatch Close)]
                            Button.create [Button.content Icons.accept; Button.onClick (fun _ -> dispatch Save)]
                        ]
                    ]
                ]
            ]

        let view model dispatch =
            Grid.create [
                Grid.columnDefinitions "2*,1*"
                Grid.children [
                    Border.create [
                        Grid.column 0
                        Border.borderThickness 1.
                        Border.padding 5.
                        Border.cornerRadius 1.
                        Border.borderBrush Brushes.LightBlue
                        Border.background Brushes.Transparent
                        Border.clipToBounds true
                        Border.child (taskEdit model dispatch )
                    ]                   
                    Border.create [
                        Grid.column 1
                        Border.borderThickness 1.
                        Border.padding 5.
                        Border.cornerRadius 1.
                        Border.borderBrush Brushes.LightBlue
                        Border.background Brushes.Transparent
                        Border.clipToBounds true
                        Border.child (TaskRunner.view (model.task,model.running,(MsgFromRunner>>dispatch),model.dispatchToRunner) )
                    ]                   
                ]
            ]

    // The Component wrapper: uses useElmish to run the MVU loop internally
    let view (win:HostWindow,n:ONode,tcs:TaskCompletionSource<(ONode*ONode) option>) =
        Component( fun ctx ->            
            let t = match n with ONode.Leaf t -> t | _ -> failwith "expecting leaf node"
            let task = ctx.useState t
            let running = ctx.useState false
            let model, dispatch = ctx.useElmish (TaskTester.init (n,task,running),  TaskTester.update (win,tcs) )
            // The view renders the current state and dispatch function
            let v : IView = TaskTester.view model dispatch
            v
        )        

type TaskTester(n:ONode) as this =
    inherit HostWindow()
    let tcs = new TaskCompletionSource<(ONode*ONode) option>()

    do
        base.Title <- "Task Tester"
        base.Width <- 400.0
        base.Height <- 600.0

        this.Content <- TaskTester.view  (this,n,tcs)

    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
