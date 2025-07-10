namespace FsOpPlanEditor

[<AutoOpen>]
module TextSticker =
    open Avalonia.FuncUI.Types
    open Avalonia.FuncUI.Builder
    open AvaloniaGraphControl
    open Avalonia.FuncUI.DSL

    let create (attrs: IAttr<TextSticker> list): IView<TextSticker> =
        ViewBuilder.Create<TextSticker>(attrs)

    type TextSticker with
        static member shape<'t when 't :> TextSticker>(value:TextSticker.Shapes) : IAttr<'t> =
          AttrBuilder<'t>.CreateProperty<TextSticker.Shapes>(TextSticker.ShapeProperty, value, ValueNone)

        static member text<'t when 't :> TextSticker>(value:string) : IAttr<'t> =
          AttrBuilder<'t>.CreateProperty<string>(TextSticker.TextProperty, value, ValueNone)
