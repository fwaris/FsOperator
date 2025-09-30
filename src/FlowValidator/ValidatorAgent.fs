namespace FlowValidator

module ValidatorAgent =
    open FsOpCore
    open System.Threading
    type State  = {driver:IUIDriver; flowRun:FlowRun}

    let rec run (stepper: WaitHandle) state = async {
        match state.flowRun.ToDo with 
        | [] -> return state
        | _ -> 
            let! signaled = Async.AwaitWaitHandle(stepper, Timeout.Infinite)
            if not signaled then
                Log.info $"timeout on wait signal"
                return state
            else
                do! Async.Sleep 1000
                try
                    let! fl = Flows.step state.driver state.flowRun
                    return! run stepper {state with flowRun=fl}
                with ex -> 
                    Log.exn(ex,nameof(run))
                    return raise ex
    }
