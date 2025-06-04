namespace FsOperator
open Microsoft.Playwright

type MouseButton =
    | Left
    | Right
    | Middle


type IUserInteraction =
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
    end


[<ReferenceEquality>]
type UserInterface =
    | Pw of {|postUrl:string->Async<unit>; driver:IUserInteraction |}
    | Na of {|driver:IUserInteraction|}
    with member this.driver = match this with Pw u -> u.driver | Na u -> u.driver
