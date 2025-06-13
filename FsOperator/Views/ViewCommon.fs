namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.Media.Imaging
open Avalonia.Platform

module Cache =
    let nav : Ref<TextBox> = ref Unchecked.defaultof<_>
    let scrollViewText : Ref<ScrollViewer> = ref Unchecked.defaultof<_>
    let scrollViewVoice : Ref<ScrollViewer> = ref Unchecked.defaultof<_>
    let splitView : Ref<SplitView> = ref Unchecked.defaultof<_>
    let textQuestion : Ref<TextBox> = ref Unchecked.defaultof<_>

    let saveIcon = lazy new Bitmap(AssetLoader.Open(Uri("avares://FsOperator/Assets/save.png")))

    let opTaskTexts : Ref<TextBox> list = 
        [for _ in 1 .. ((FSharp.Reflection.FSharpType.GetRecordFields typeof<OpTask>).Length - 1) -> //textboxes for all fields except id  
            (ref Unchecked.defaultof<_>)]
    

module Icons =
    let iconFont = "Segoe UI Emoji"
    let stop = "\U0001F6D1"
    let start =  "\U0001F7E2"
    let save = "\U0001F4BE"
    let edit = "\u270E"
    let report = "\U0001F4CB"
    let send = "\u27a1"
