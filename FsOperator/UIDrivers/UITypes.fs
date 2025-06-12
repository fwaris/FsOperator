namespace FsOperator
open FsOpCore
open Microsoft.Playwright

module K = 
    let [<Literal>] Enter = "Enter"
    let [<Literal>] Backspace = "Backspace"
    let [<Literal>] Escape = "Escape"
    let [<Literal>] Shift = "Shift"
    let [<Literal>] Control = "Control"
    let [<Literal>] Tab = "Tab"
    let [<Literal>] ArrowLeft = "ArrowLeft"
    let [<Literal>] ArrowRight = "ArrowRight"
    let [<Literal>] ArrowUp = "ArrowUp"
    let [<Literal>] ArrowDown = "ArrowDown"
    let [<Literal>] Alt = "Alt"
    let [<Literal>] AltGraph = "AltGraph"
    let [<Literal>] Meta = "Meta"
    let [<Literal>] PageUp = "PageUp"
    let [<Literal>] PageDown = "PageDown"
    let [<Literal>] Home = "Home"
    let [<Literal>] End = "End"
    let [<Literal>] Insert = "Insert"
    let [<Literal>] Delete = "Delete"

module DriverUtils = 
        
    let canonicalize keys =
        keys
        |> List.map (fun k ->
            if k =*= "Enter" then K.Enter
            elif k =*= "space" then " "
            elif k =*= "backspace" then K.Backspace
            elif k =*= "ESC" then K.Escape
            elif k =*= "SHIFT" then K.Shift
            elif k =*= "CTRL" then K.Control
            elif k =*= "TAB" then K.Tab
            elif k =*= "ArrowLeft" then K.ArrowLeft
            elif k =*= "ArrowRight" then K.ArrowRight
            elif k =*= "ArrowUp" then K.ArrowUp
            elif k =*= "ArrowDown" then K.ArrowDown
            elif k =*= "ALT" then K.Alt
            elif k =*= "ALTGR" then K.AltGraph
            elif k =*= "META" then K.Meta
            elif k =*= "PAGEUP" then K.PageUp
            elif k =*= "PAGEDOWN" then K.PageDown
            elif k =*= "HOME" then K.Home 
            elif k =*= "END" then K.End
            elif k =*= "INSERT" then K.Insert
            elif k =*= "DELETE" then K.Delete
            else k)

type MouseButton =
    | Left
    | Right
    | Middle

type IUIDriver =
    interface
        abstract member doubleClick: x:int*y:int -> Async<unit>
        abstract member click : x:int*y:int*MouseButton -> Async<unit>
        abstract member typeText : string -> Async<unit>
        abstract member wheel : x:int*y:int -> Async<unit>
        abstract member move : x:int*y:int -> Async<unit>
        abstract member scroll : x:int*y:int -> scrollX:int*scrollY:int -> Async<unit>
        abstract member pressKeys : keys:string list -> Async<unit>
        abstract member dragDrop : sourceX:int*sourceY:int -> targetX:int*targetY:int -> Async<unit>
        abstract member snapshot : unit -> Async<string*(int*int)>
        abstract member goBack : unit -> Async<unit>
        abstract member goForward : unit -> Async<unit>
        abstract member url : unit -> Async<string option>
        abstract member environment : string //computer call envrionment
    end

[<ReferenceEquality>]
type UserInterface =
    | Pw of {|postUrl:string->Async<unit>; driver:IUIDriver |}
    | Na of {|driver:IUIDriver; processName:string; arg:string option|}
    with member this.driver = match this with Pw u -> u.driver | Na u -> u.driver
