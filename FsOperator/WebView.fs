namespace WebViewControl.Ext
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open WebViewControl
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Builder

[<AutoOpen>]
module WebView =
    let ginit() =
        let g = GlobalSettings()
        ()


    let private stringsEqual(a : obj, b : obj) =
        (a :?> string) = (b :?> string)

    type WebView with
        static member create(attrs : IAttr<WebView> list): IView<WebView> =
            ViewBuilder.Create<WebView>(attrs)

        static member address(address : string) =
            AttrBuilder<WebView>.CreateProperty<string>(WebView.AddressProperty, address, stringsEqual |> ValueOption<_>.Some)

        // static member devtools(address : string) =
        //     AttrBuilder<WebView>.CreateProperty<string>(WebView., address, stringsEqual |> ValueOption<_>.Some)
(*
[<AutoOpen>]
module WebView =
    open System
    open AvaloniaWebView
    open Avalonia.FuncUI.Builder
    open Avalonia.FuncUI.Types
    open Avalonia.FuncUI.DSL

    let create (attrs: IAttr<WebView> list): IView<WebView> =
        ViewBuilder.Create<WebView>(attrs)

    type WebView with
        static member url(value:Uri) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<Uri>(WebView.UrlProperty, value, ValueNone)

        static member htmlContent(value:string) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<string>(WebView.HtmlContentProperty, value, ValueNone)
*)
