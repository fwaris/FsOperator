open FlowValidator
open System

module Pgm =
    open FsOpCore
    open System.Threading

    if not (System.IO.Directory.Exists(PlaywrightDriver.downloadsPath.Value)) then
        System.IO.Directory.CreateDirectory(PlaywrightDriver.downloadsPath.Value) |> ignore
    //Sandbox.test()

    [<EntryPoint>] 
    let main args =         
        use stepper = new AutoResetEvent(false)
        let flowR = FlowRun.FromFlow (FlowDefs.vzFlow (Environment.GetEnvironmentVariable("PORT_OUT_URL_2")))
        let state = {ValidatorAgent.State.driver = (PlaywrightDriver.create()).driver; ValidatorAgent.State. flowRun=flowR}
        ValidatorAgent.run stepper state |> Async.Ignore |> Async.Start
        printfn "Hit Enter to step and ESC to quit"
        let rec readCommandKey() = 
            let k = System.Console.ReadKey(true) // true = don't echo the key
            match k.KeyChar with 
            | 's' -> stepper.Set() |> ignore; readCommandKey()
            | 'q' -> ()
            | _ -> printf "."; readCommandKey()
        
        readCommandKey()
        0
