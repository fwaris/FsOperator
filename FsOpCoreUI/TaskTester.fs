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
open System.Threading.Channels
open Avalonia.FuncUI.Hosts

module TaskTester = 
    type MsgOut = Update of (ONode*ONode) //old update node
    
    module internal TaskTester =
        type Msg = StartTest | StopTest | Update of ONode
        type Model = 
            {
                    refNode          : ONode
                    task             : IWritable<OTask>
                    dispatchToRunner : Ref<TaskRunner.MsgIn -> unit>
            }


        let init (n:ONode,task:IWritable<OTask>) ()=             
            {
                refNode = n
                task = task
                dispatchToRunner = ref(fun _ -> ())
            }, 
            Cmd.none

        let update (dispatchOut:MsgOut -> unit) msg model = 
            match msg with 
            | StartTest -> model,Cmd.none

        let taskEdit model dispatch = 
            let cache : Ref<TextBox> list = 
                [for _ in 1 .. ((FSharp.Reflection.FSharpType.GetRecordFields typeof<OTask>).Length - 1) -> //textboxes for all fields except id  
                    (ref Unchecked.defaultof<_>)]
            let task = model.task.Current
            Grid.create [
                Grid.rowDefinitions "*,*,*,*,*"
                Grid.columnDefinitions "100,*"
                Grid.width 400.
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
            |> fun g -> 
                Border.create [
                    Border.borderThickness 1.
                    Border.padding 5.
                    Border.cornerRadius 1.
                    Border.borderBrush Brushes.LightBlue
                    Border.background Brushes.Transparent
                    Border.clipToBounds true
                    Border.child g
                ]
            :> IView

        let view model dispatch =
            Grid.create [
                Grid.columnDefinitions "2*,1*"
                Grid.children [
                    taskEdit model dispatch
                    Panel.create [
                        Grid.column 1
                        Panel.children [
                            TaskRunner.view (model.task,(fun x ->()),(ref(fun x->()))) 
                        ]
                    ]
                ]
            ]

    // The Component wrapper: uses useElmish to run the MVU loop internally
    let view (n:ONode,dispatchOut:MsgOut->unit) =
        Component( fun ctx ->
            let t = match n with ONode.Leaf t -> t | _ -> failwith "expecting leaf node"
            let state = ctx.useState t
            let post = ref(fun m -> ())
            let sub _ = Subscriptions.create $"ta.{n.displayStr()}" post
            let model, dispatch = ctx.useElmish (TaskTester.init (n,state),  TaskTester.update dispatchOut, Program.withSubscription sub)
            // The view renders the current state and dispatch function
            let v : IView = TaskTester.view model dispatch
            v
        )
        

type TaskTester(n:ONode) as this =
    inherit HostWindow()
    let tcs = new System.Threading.Tasks.TaskCompletionSource<ONode*ONode>()

    do
        base.Title <- "Task Tester"
        base.Width <- 400.0
        base.Height <- 600.0

        this.Content <- TaskTester.view  (n,fun _ ->())

    member this.ShowDialogAsync(parent: Window) =
        base.ShowDialog(parent) |> ignore
        tcs.Task
