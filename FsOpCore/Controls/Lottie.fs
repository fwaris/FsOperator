namespace Avalonia.Labs.Lottie.Ext
open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open Avalonia
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.Builder
open Avalonia.Labs.Lottie
open Avalonia.Platform


[<AutoOpen>]
module Lottie =
    
    let assets = 
        lazy(
            AssetLoader
                .GetAssets(new Uri("avares://FsOperator/Assets"), new Uri("avares://FsOperator/"))
                |> Seq.filter (fun x -> x.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                |> Seq.map (fun x -> x.AbsoluteUri)
                |> Seq.toList
        )

    let defaultPath = lazy(List.head assets.Value)

    let private stringsEqual(a : obj, b : obj) =
        (a :?> string) = (b :?> string)

    type Lottie with 
        static member create(attrs : IAttr<Lottie> list): IView<Lottie> =
            ViewBuilder.Create<Lottie>(attrs)
            |> View.withConstructorArgs [|new Uri("avares://Avalonia.Labs.Catalog/Assets") :> obj|]

        static member path(value : string) =
            AttrBuilder<Lottie>.CreateProperty<string>(Lottie.PathProperty, value, stringsEqual |> ValueOption<_>.Some)

        static member repeatCount(value : int) =
            AttrBuilder<Lottie>.CreateProperty<int>(Lottie.RepeatCountProperty, value, ValueNone )


(*
module WebView =
    let ginit() =
        let g = GlobalSettings()
        ()



    type WebView with
        static member create(attrs : IAttr<WebView> list): IView<WebView> =
            ViewBuilder.Create<WebView>(attrs)

        static member address(address : string) =
            AttrBuilder<WebView>.CreateProperty<string>(WebView.AddressProperty, address, stringsEqual |> ValueOption<_>.Some)
 

        // static member devtools(address : string) =


*)