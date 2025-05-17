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

    let saveFileDialog (parent: Window) =
        async {
            // Initialize the folder picker dialog options
            let options = FilePickerSaveOptions(
                Title = "Save FsOperator Task",
                FileTypeChoices = filters
            )

            let! file = parent.StorageProvider.SaveFilePickerAsync(options) |> Async.AwaitTask

            return match file with null -> None | _ -> Some (file.TryGetLocalPath())
        }
