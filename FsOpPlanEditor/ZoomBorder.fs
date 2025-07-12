namespace FsOpPlanEditor

open Avalonia.Controls.PanAndZoom
open Avalonia.FuncUI.DSL

[<AutoOpen>]
module ZoomBorder =
    open Avalonia.FuncUI.Types
    open Avalonia.FuncUI.Builder
  
    let create (attrs: IAttr<ZoomBorder> list): IView<ZoomBorder> =
        ViewBuilder.Create<ZoomBorder>(attrs)
    
    type ZoomBorder with
        static member child<'t when 't :> ZoomBorder>(value: IView option) : IAttr<'t> =
            AttrBuilder<'t>.CreateContentSingle(ZoomBorder.ChildProperty, value)

        static member enablePan<'t when 't :> ZoomBorder>(value: bool) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<bool>(ZoomBorder.EnablePanProperty, value,ValueNone)

        static member enableZoom<'t when 't :> ZoomBorder>(value: bool) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<bool>(ZoomBorder.EnableZoomProperty, value,ValueNone)

        static member panButton<'t when 't :> ZoomBorder>(value: ButtonName) : IAttr<'t> =            
            AttrBuilder<'t>.CreateProperty<ButtonName>(ZoomBorder.PanButtonProperty, value,ValueNone)
