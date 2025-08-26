namespace FsOpPlanEditor
open Avalonia.FuncUI.DSL
open Avalonia.Controls
open AvaloniaGraphControl
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Builder
open Microsoft.Msagl.Drawing
open AvaloniaGraphControl

[<AutoOpen>]
module GraphPanel =
    open Avalonia.Controls.Templates

    let create (attrs: IAttr<GraphPanel> list) : IView<GraphPanel> =
        ViewBuilder.Create<GraphPanel>(attrs)

    type GraphPanel with

        static member graph(value : Graph) :IAttr<GraphPanel> =
            AttrBuilder<GraphPanel>.CreateProperty<Graph>(GraphPanel.GraphProperty, value, ValueNone)

        static member layoutMethods(value: GraphPanel.LayoutMethods) : IAttr<GraphPanel> =
            AttrBuilder<GraphPanel>.CreateProperty<GraphPanel.LayoutMethods>(GraphPanel.LayoutMethodProperty, value, ValueNone)

        static member dataTemplates<'t when 't :> GraphPanel>(value: DataTemplates) : IAttr<'t> =
          let getter : ('t -> DataTemplates) = (fun t -> if t.DataTemplates = null then new DataTemplates() else t.DataTemplates)
          let setter : ('t * DataTemplates -> unit) = (fun (t,v) -> t.DataTemplates.Clear(); t.DataTemplates.AddRange(v))
          AttrBuilder<'t>.CreateProperty<DataTemplates>("DataTemplates", value, ValueSome getter, ValueSome setter, ValueNone)

