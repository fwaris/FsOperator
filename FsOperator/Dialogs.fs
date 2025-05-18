namespace FsOperator
open Avalonia
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.Layout
open System.Threading.Tasks
open Avalonia.FuncUI
open Avalonia.Controls
open Avalonia.Platform.Storage

module Dialogs =

    let filters= [|
                    FilePickerFileType("FsOperator Task", Patterns = [| ".optask"; ".json" |])
                 |]

    let openFileDialog (parent: Window) =
        async {
            // Initialize the folder picker dialog options
            let options = FilePickerOpenOptions(
                Title = "Load FsOperator Task",
                AllowMultiple = false,
                FileTypeFilter = filters
            )

            let! files = parent.StorageProvider.OpenFilePickerAsync(options) |> Async.AwaitTask

            // Process the selected files
            return
                files
                |> Seq.tryHead
                |> Option.map _.TryGetLocalPath()                    
        }

    let saveFileDialog (parent: Window) proposedName =
        async {
            // Initialize the folder picker dialog options
            let options = FilePickerSaveOptions(
                Title = "Save FsOperator Task",
                FileTypeChoices = filters,
                SuggestedFileName = (proposedName |> Option.defaultValue null),
                DefaultExtension = ".optask"
            )

            let! file = parent.StorageProvider.SaveFilePickerAsync(options) |> Async.AwaitTask

            return match file with null -> None | _ -> Some (file.TryGetLocalPath())
        }

type YesNoDialog(message: string) as this =
    inherit HostWindow()
    let tcs = new TaskCompletionSource<bool>()
    do
        base.Title <- "Confirmation"
        base.Width <- 400.0
        base.Height <- 150.0

        let content =            
            DockPanel.create [
                DockPanel.children [
                    TextBlock.create [
                        TextBlock.text message
                        TextBlock.margin (Thickness 10.0)
                        TextBlock.verticalAlignment VerticalAlignment.Center
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                    ]
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                        StackPanel.children [
                            Button.create [
                                Button.content "Yes"
                                Button.margin (Thickness 5.0)
                                Button.onClick (fun _ ->
                                    tcs.SetResult(true)
                                    this.Close()
                                )
                            ]
                            Button.create [
                                Button.content "No"
                                Button.margin (Thickness 5.0)
                                Button.onClick (fun _ ->
                                    tcs.SetResult(false)
                                    this.Close()
                                )
                            ]
                        ]
                    ] 
                ]
                DockPanel.dock Dock.Bottom
            ]

        this.Content <- Component(fun ctx -> content)
    member this.ShowDialogAsync(parent: Window) : Task<bool> =
        base.ShowDialog(parent) |> ignore
        tcs.Task

