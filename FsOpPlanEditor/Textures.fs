namespace FsOpPlanEditor
    
module Textures = 
    open System
    open Avalonia.Media
    open Avalonia.Media.Imaging
    open Avalonia.Platform

    let grip =
        let uri = Uri("avares://FsOpPlanEditor/Assets/grip.png")
        use stream = AssetLoader.Open(uri)
        let bitmap = new Bitmap(stream)
        bitmap
        //new ImageBrush(bitmap)

module Cursors = 
    open Avalonia.Input
    let hand = new Cursor(StandardCursorType.Hand)

