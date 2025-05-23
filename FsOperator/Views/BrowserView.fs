namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia
open Avalonia.Svg.Skia
open Avalonia.Controls.Shapes
open Avalonia.FuncUI
open Avalonia.Labs.Lottie
open Avalonia.Labs.Lottie.Ext
open FsOpCore
open Avalonia.Platform

module Cache =
    let nav : Ref<TextBox> = ref Unchecked.defaultof<_>
    let scrollViewText : Ref<ScrollViewer> = ref Unchecked.defaultof<_>
    let scrollViewVoice : Ref<ScrollViewer> = ref Unchecked.defaultof<_>
    let splitView : Ref<SplitView> = ref Unchecked.defaultof<_>
    let saveIcon = lazy new Bitmap(AssetLoader.Open(Uri("avares://FsOperator/Assets/save.png")))
    let floppyIcon = lazy SvgImage(Source = SvgSource.Load("avares://FsOperator/Assets/floppy.svg", null))

    let opTaskTexts : Ref<TextBox> list = 
        [for _ in 1 .. ((FSharp.Reflection.FSharpType.GetRecordFields typeof<OpTask>).Length - 1) -> //textboxes for all fields except id  
            (ref Unchecked.defaultof<_>)]

[<AbstractClass; Sealed>]
type BrowserView =

    static member navigationBar model dispatch = 
        let csState = model.taskState |> Option.map (fun rs -> rs.cuaState) |> Option.defaultValue CUAState.CUA_Init
        let actionBg = 
            if model.isFlashing 
            then Brushes.DarkOrange 
            elif (TaskState.cuaMode model.taskState).IsCUA_Pause 
            then  Brushes.DarkRed
            else Brushes.DarkSlateBlue
        Border.create [
            Grid.row 0
            Border.borderThickness 1.0
            Border.margin 2.0
            Border.borderBrush Brushes.LightBlue
            Border.verticalAlignment VerticalAlignment.Top
            Border.horizontalAlignment HorizontalAlignment.Stretch
            Border.child (        
                Grid.create [
                    Grid.columnDefinitions "65*,35*"
                    Grid.horizontalAlignment HorizontalAlignment.Stretch
                    Grid.verticalAlignment VerticalAlignment.Stretch
                    Grid.children [                    
                        TextBox.create [
                            Grid.column 0
                            TextBox.init (fun x -> Cache.nav.Value <- x)
                            TextBox.text (model.opTask.url)
                            TextBox.borderThickness 0.
                            TextBox.margin 5
                            TextBox.watermark "Enter URL here"
                            TextBox.verticalAlignment VerticalAlignment.Center
                            TextBox.horizontalAlignment HorizontalAlignment.Stretch
                            TextBox.onKeyDown (fun e -> 
                                if e.Key = Avalonia.Input.Key.Enter then
                                    if Cache.nav.Value<> Unchecked.defaultof<_> then
                                        let url = Cache.nav.Value.Text
                                        dispatch (OpTask_SetUrl url)
                                    else
                                        debug("URL is empty")            
                            )
                        ]
                        TextBlock.create [
                                Grid.column 1
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                TextBlock.background actionBg
                                TextBlock.text model.action
                                TextBlock.margin (Thickness(1,1,5,1))
                                TextBlock.padding 3
                        ]

                    ]

                ]
            )    
        ]
        
    static member internalWebView model dispatch = 
        TextBlock.create [
            Grid.row 1
            TextBlock.text " webview"
        ]
        // WebView.create [
        //    Visual.zIndex -21
        //    Grid.row 1
        //    (*
        //    WebView.height 725
        //    WebView.width 1285
        //    *)
        //    WebView.maxHeight 725
        //    WebView.maxWidth 1285
        //    WebView.address model.url
        //    WebView.init (fun wv ->
        //        match model.webview.Value with
        //        | Some _ -> ()
        //        | None -> 
        //            model.webview.Value <- Some wv
        //            wv.Initialized.Add (fun args ->     
        //                    task {
        //                        try
        //                            let! browser = Connection.connection()
        //                            dispatch BrowserConnected
        //                        with ex ->                                                                         
        //                            debug (sprintf "%A" ex)
        //                    }
        //                    |> ignore                            
        //                    ()
        //        ))
        //]                                

    static member webview model dispatch =
        BrowserView.internalWebView model dispatch
        (*
        Grid.create [
            Grid.row 1
            Visual.zIndex -1
            Grid.clipToBounds true
            Grid.verticalAlignment VerticalAlignment.Stretch
            Grid.horizontalAlignment HorizontalAlignment.Stretch
            Grid.children [BrowserView.internalWebView model dispatch]
        ]
        *)
