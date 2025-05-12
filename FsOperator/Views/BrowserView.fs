namespace FsOperator
open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open WebViewControl
open WebViewControl.Ext
open Avalonia.Controls.Shapes
open Avalonia.FuncUI
open Avalonia.Labs.Lottie
open Avalonia.Labs.Lottie.Ext

module Cache =
    let nav : Ref<TextBox> = ref Unchecked.defaultof<_>


[<AbstractClass; Sealed>]
type BrowserView =

    static member navigationBar model dispatch =
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
                            TextBox.text (model.url.ToString())
                            TextBox.borderThickness 0.
                            TextBox.margin 5
                            TextBox.verticalAlignment VerticalAlignment.Center
                            TextBox.horizontalAlignment HorizontalAlignment.Stretch
                            TextBox.onKeyDown (fun e ->
                                if e.Key = Avalonia.Input.Key.Enter then
                                    if Cache.nav.Value<> Unchecked.defaultof<_> then
                                        let url = Cache.nav.Value.Text
                                        if Uri.IsWellFormedUriString(url, UriKind.Absolute) then
                                            dispatch (SetUrl url)
                                        else
                                            debug($"Invalid URL: {url}")
                                    else
                                        debug("URL is empty")
                            )
                        ]
                        TextBlock.create [
                                Grid.column 1
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.horizontalAlignment HorizontalAlignment.Stretch
                                TextBlock.background (if model.isFlashing then Brushes.DarkSalmon else Brushes.DarkSlateBlue)
                                TextBlock.text model.action
                                TextBlock.margin (Thickness(1,1,5,1))
                                TextBlock.padding 3
                        ]
                    ]

                ]
            )
        ]

    static member internalWebView model dispatch =
         WebView.create [
            Visual.zIndex -21
            Grid.row 1
            (*
            WebView.height 725
            WebView.width 1285
            *)
            WebView.maxHeight 725
            WebView.maxWidth 1285
            WebView.address model.url
            WebView.init (fun wv ->
                match model.webview.Value with
                | Some _ -> ()
                | None ->
                    model.webview.Value <- Some wv
                    model.webview.Value <- Some wv
                    wv.add_PopupOpening(fun p ->
                        dispatch (SetUrl p)
                    )
                    wv.Initialized.Add (fun args ->
                            task {
                                try
                                    let! browser = Connection.connection()
                                    dispatch BrowserConnected
                                with ex ->
                                    debug (sprintf "%A" ex)
                            }
                            |> ignore
                            ()
                ))
        ]

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
