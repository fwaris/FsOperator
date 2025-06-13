#load "packages.fsx"
open FsOpCore

let post x = printfn "%A" x
let ui = PlaywrightDriver.create()
let ch = Chat.Default

let fl = TaskFlow.create post ui.driver ch
fl.Post TaskFlow.TFi_Start
fl.Terminate()
