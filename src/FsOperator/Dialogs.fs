namespace FsOperator
open System.Collections.Generic
open Avalonia
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.Layout
open System.Threading.Tasks
open Avalonia.FuncUI
open Avalonia.Controls
open Avalonia.Platform.Storage
open Avalonia.Media
open Avalonia.Threading

module Dialogs =

    let filters= [|
                    FilePickerFileType("FsOperator Task", Patterns = [| "*.optask"; "*.json"|])
                 |]

    let openFileDialog (parent: Window) =
        async {
            // Initialize the folder picker dialog options
            let options = FilePickerOpenOptions(
                Title = "Load FsOperator Task",
                AllowMultiple = false,
                FileTypeFilter = filters
            )

            let fileTask = Dispatcher.UIThread.InvokeAsync<IReadOnlyList<IStorageFile>>(fun _ -> 
                parent.StorageProvider.OpenFilePickerAsync(options))
            let! files = fileTask |> Async.AwaitTask

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
            let fileTask = Dispatcher.UIThread.InvokeAsync<IStorageFile>(fun _ ->
                parent.StorageProvider.SaveFilePickerAsync(options)
                )
            let! file = fileTask |> Async.AwaitTask
            return match file with null -> None | _ -> Some (file.TryGetLocalPath())
        }

type YesNoDialog(message: string) as this =
    inherit HostWindow()
    let tcs = new TaskCompletionSource<bool>()
    do
        base.Title <- "Confirmation"
        base.Width <- 400.0
        base.Height <- 150.0
        base.SystemDecorations <- SystemDecorations.BorderOnly

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
            |> fun g -> Border.create [
                Border.child g
                Border.borderThickness 1.0
                Border.padding 5.0
                Border.cornerRadius (CornerRadius(5.0))
                Border.borderBrush Brushes.LightBlue
            ]

        this.Content <- Component(fun ctx -> content)
    member this.ShowDialogAsync(parent: Window) : Task<bool> =
        base.ShowDialog(parent) |> ignore
        tcs.Task

