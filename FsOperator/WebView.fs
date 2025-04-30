namespace AvaloniaWebView.Ext

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
