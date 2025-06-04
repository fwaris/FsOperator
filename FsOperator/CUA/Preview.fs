namespace FsOperator
open System
open FSharp.Control
open PuppeteerSharp
open FsResponses
open System.IO
open PuppeteerSharp.Input
open FsOpCore


module Preview =
    ()
    //let drawClick (x:int) (y:int) (duration:int) =
    //    async {
    //        let script = Scripts.drawCircle x y 20 0.7 duration
    //        let! page = Browser.page()
    //        let ts1 = DateTime.Now
    //        let! _  =  page.EvaluateFunctionAsync($"()=> {script}") |> Async.AwaitTask
    //        let ts2 = DateTime.Now
    //        debug $"drawArrow took {ts2-ts1} ms"
    //        do! Async.Sleep(duration)
    //    }

    //let drawArrow (x:int) (y:int) (length:int) (angle:float) (duration:int) =
    //    async {
    //        let id = newId()
    //        let script = $"""{Scripts.drawArrow}('{id}',{x}, {y}, {length}, {angle}, {duration})"""
    //        let! page = Browser.page()
    //        let frame = page.MainFrame
    //        let ts1 = DateTime.Now
    //        let opts = WaitForFunctionOptions()
    //        opts.Polling <- WaitForFunctionPollingOption.Raf
    //        opts.Timeout <- duration + 100
    //        opts.PollingInterval <- 100
    //        let! _ = frame.WaitForExpressionAsync(script,opts) |> Async.AwaitTask
    //        //let! _ = page.EvaluateExpressionAsync(script) |> Async.AwaitTask
    //        let ts2 = DateTime.Now
    //        debug $"drawArrow took {ts2-ts1} ms"
    //        return ()
    //    }

    //let drawDragArrow (x1:int,y1:int) (x2:int,y2:int) (duration:int) =
    //    async {
    //        let script = $"""{Scripts.drawDragArrow}({x1},{y2},{x2},{y2}, {duration})"""
    //        let! page = Browser.page()
    //        let ts1 = DateTime.Now
    //        let! _ = page.WaitForExpressionAsync(script) |> Async.AwaitTask
    //        let ts2 = DateTime.Now
    //        debug $"drawArrow took {ts2-ts1} ms"
    //        return ()
    //    }


(*
    let previewAction (duration:int) (action:Action) =
        //async { return ()}
        async {
            try

                let! page = Browser.page()
                match action with
                | Click p ->
    //                do! drawClick p.x p.y duration
                    do! Async.Sleep(duration)
                (*
                | Scroll p ->
                    let degrees =
                        match p.scroll_x,p.scroll_y with
                        | x,y when x = 0 && y < 0 -> 3. * Math.PI / 2.
                        | x,y when x = 0          -> Math.PI / 2.
                        | x,y when x < 0          -> Math.PI
                        | _                       -> 0.
                    do! drawArrow 100 200 40 degrees duration
                    do! Async.Sleep(duration)
                | Drag p ->
                    let s = p.path.Head
                    let t = List.last p.path
                    do! drawDragArrow (s.x,s.y) (t.x,t.y) duration
                    do! Async.Sleep(duration)
                *)
                | _ -> ()
            with ex ->
                do! Browser.closeConnection()
                debug $"Error in previewAction: %s{ex.Message}"
        }
*)
