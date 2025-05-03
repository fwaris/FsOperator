namespace FsOperator
open System
open FSharp.Control
open Microsoft.Playwright
open FsResponses
open System.IO

module ComputerUse =
    open Microsoft.Playwright

    let postLog (runState:RunState) msg =  runState.mailbox.Writer.TryWrite(ClientMsg.AppendLog msg) |> ignore
    let postAction (runState:RunState) action = runState.mailbox.Writer.TryWrite(SetAction action) |> ignore
    let postWarning (runState:RunState) warning = runState.mailbox.Writer.TryWrite(SetWarning warning) |> ignore

    let rec sendWithRetry count (runState:RunState) (req:Request) =
        async {
            try
                let! response = Api.create req (Api.defaultClient()) |> Async.AwaitTask
                return response
            with ex ->
                if count < 2 then 
                    postLog runState $"send error: retry {count + 1}"
                    return! sendWithRetry (count + 1) runState req
                else
                    postLog runState $"Unable to reconnect aborting"
                    return raise ex
        }
  
    let startMessaging (runState:RunState) =
        let sendLoop = 
            runState.toModel.Reader.ReadAllAsync(runState.tokenSource.Token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun request ->
                async {
                    postLog runState $"--> {request}"
                    let! response = sendWithRetry 0 runState request                    
                    postLog runState $"<-- {response}"    
                    do! runState.fromModel.Writer.WriteAsync(response,runState.tokenSource.Token).AsTask() |> Async.AwaitTask
                }
            )
        let comp = 
            async {
                match! Async.Catch sendLoop with 
                | Choice1Of2 _ -> debug "dispose sendLoop"
                | Choice2Of2 ex -> 
                    debug $"Error in sendLoop: %s{ex.Message}"                    
                    runState.mailbox.Writer.TryWrite(StopWithError ex) |> ignore
            }
        Async.Start(comp, runState.tokenSource.Token)

    let snapshot (browser:IBrowser) = 
        async {
            let wctx = browser.Contexts.[0]
            let page = wctx.Pages.[0]
            let opts = PageScreenshotOptions()
            opts.Type <- ScreenshotType.Png
            let! image = page.ScreenshotAsync() |> Async.AwaitTask
            use ms = new MemoryStream(image)
            use bmp = System.Drawing.Image.FromStream(ms)
            let imgUrl = image |> RUtils.toImageUri
            return imgUrl,(int bmp.PhysicalDimension.Width, int bmp.PhysicalDimension.Height)
        }

    let sendStartMessage (runState:RunState) =
       async {
                let imgUrl,(w,h) = snapshot runState.browser |> Async.RunSynchronously
                let contImg = Input_image {|image_url = imgUrl|}
                let input = { Message.Default with content=[contImg]}
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                let instructions,prev_id = 
                    match runState.lastResponse.Value with 
                    | None   -> Some runState.instructions, None  //no previous response, i.e. first msg,send the instructions
                    | Some r -> None, Some r.id                   //otherwise send the previous response id
                let req = {Request.Default with 
                                input = [Message input]; tools=[tool]
                                instructions = instructions
                                previous_response_id = prev_id                               
                                store = true
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }
                do! runState.toModel.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
        }

    let getResponseIdsAndChecks (runState:RunState) = 
        runState.lastResponse.Value
        |> Option.bind (fun r -> 
            let safetyChecks = r.output |> List.choose (function Computer_call cb -> Some cb.pending_safety_checks | _ -> None) |> List.concat
            r.output
            |> List.choose (function
                | Computer_call cb -> Some cb.call_id
                | _ -> None)
            |> List.rev
            |> List.tryHead
            |> Option.map (fun cbId -> r.id, cbId, safetyChecks) //return the response id and the computer call id)
        )

    let sendNext (runState:RunState) =
        async {
            match getResponseIdsAndChecks runState with
            | None -> 
                runState.mailbox.Writer.TryWrite(AppendLog "turn end") |> ignore
                runState.mailbox.Writer.TryWrite(TurnEnd) |> ignore
                return ()
            | Some (prevId, lastCallId, safetyChecks) -> 
                let imgUrl,(w,h) = snapshot runState.browser |> Async.RunSynchronously
                let contImg = Input_image {|image_url = imgUrl|} //use the same image url as before
                let tool = Tool_Computer_use {|display_height = h; display_width = w; environment = ComputerEnvironment.browser|}
                debug $"snapshot dims : {w}, {h}"
                let cc_out = {
                    call_id = lastCallId
                    acknowledged_safety_checks = safetyChecks                                 //these should come from human acknowlegedgement
                    output = Computer_creenshot {|image_url = imgUrl |}
                    current_url = Some runState.browser.Contexts.[0].Pages.[0].Url
                }
                let req = {Request.Default with 
                                input = [Computer_call_output cc_out]; tools=[tool]
                                previous_response_id = Some prevId
                                store = true
                                model=Models.computer_use_preview
                                truncation = Some Truncation.auto
                          }

                do! runState.toModel.Writer.WriteAsync(req).AsTask() |> Async.AwaitTask                    
        }

        
    let private (==) (a:string) (b:string) = a.Equals(b, StringComparison.OrdinalIgnoreCase)

    let actionToString action = 
        try
            match action with
            | Click p -> $"click({p.x},{p.y})"
            | Scroll p -> $"scroll({p.scroll_x},{p.scroll_y},{p.x},{p.y})"
            | Double_click p -> $"dbl_click({p.x},{p.y})"
            | Keypress p -> $"keys {p.keys}"
            | Move p -> $"move({p.x},{p.y})"
            | Screenshot -> "screenshot"
            | Type p -> $"type {p.text}"
            | Wait  -> "wait"
            | Drag p -> 
                let s = p.path.Head
                let t = List.last p.path
                $"drag {s.x},{s.y} -> {t.x},{t.y}"
        with ex -> 
            debug $"Error in actionToString: %s{ex.Message}"
            sprintf "%A" action


    type RequestAction = 
        | Btn of MouseButton
        | Back 
        | Forward
        | Unknown
    let mouseButton = function 
        | Buttons.Left          -> Btn MouseButton.Left
        | Buttons.Middle        -> Btn MouseButton.Middle
        | Buttons.Right         -> Btn MouseButton.Right
        | "back" | "Back"       -> Back
        | "forward" | "Forward" -> Forward
        | x -> debug $"cannot use '{x}' button"; Unknown

    let doAction (action:Action) (browser:IBrowser)  =
        let task = 
            async {
                let page = browser.Contexts.[0].Pages.[0]
                match action with 
                | Click p -> 
                    match mouseButton p.button with
                    | Btn btn -> 
                        let opts = MouseClickOptions(Button = btn)
                        let! _ = page.EvaluateAsync($"() => window.drawClick({p.x},{p.y})") |> Async.AwaitTask
                        do! Async.Sleep(1000)
                        do! page.Mouse.ClickAsync(float32 p.x,float32 p.y, opts) |> Async.AwaitTask
                    | Back -> page.GoBackAsync() |> Async.AwaitTask |> ignore
                    | Forward -> page.GoForwardAsync() |> Async.AwaitTask |> ignore
                    | Unknown -> do! Async.Sleep(500) //model is trying to use a button that is not supported
                | Scroll p ->
                    do! page.Mouse.MoveAsync(float32 p.x,float32 p.y) |> Async.AwaitTask                                
                    let! _ = page.EvaluateAsync($"window.scrollBy({p.scroll_x}, {p.scroll_y})")  |> Async.AwaitTask                                          
                    ()
                | Keypress p -> 
                    let mappeKeys = 
                        p.keys 
                        |> List.map (fun k -> 
                            if k == "Enter" then "Enter"                             //Playwright does not support Enter key
                            elif k == "space" then " "
                            elif k = "backspace" then "Backspace"
                            elif k == "ESC" then "Escape"
                            elif k == "SHIFT" then "Shift"
                            elif k == "CTRL" then "Control"
                            elif k == "TAB" then "Tab"
                            else k)
                    let compositKey = mappeKeys |> String.concat "+"
                    let opts = KeyboardPressOptions()
                    do! page.Keyboard.PressAsync(compositKey, opts) |> Async.AwaitTask                            
                | Type p ->
                    do! page.Keyboard.TypeAsync(p.text) |> Async.AwaitTask
                | Wait  ->  do! Async.Sleep(2000)
                | Screenshot -> ()
                | Move p -> do! page.Mouse.MoveAsync(float32 p.x,float32 p.y) |> Async.AwaitTask
                | Double_click p -> do! page.Mouse.DblClickAsync(float32 p.x,float32 p.y) |> Async.AwaitTask
                | Drag p ->                     
                    let s = p.path.Head
                    let t = List.last p.path 
                    do! page.Mouse.MoveAsync(float32 s.x,float32 s.y) |> Async.AwaitTask
                    do! page.Mouse.DownAsync() |> Async.AwaitTask
                    do! page.Mouse.MoveAsync(float32 t.x, float32 t.y, MouseMoveOptions(Steps=10)) |> Async.AwaitTask
            }
        async{
            match! Async.Catch task with 
            | Choice1Of2 _ -> ()
            | Choice2Of2 ex -> debug $"Error in doAction: %s{ex.Message}"
        }
                
    let loop (runState:RunState) = 
        let rec loop() = 
            async {                      
                let! response = runState.fromModel.Reader.ReadAsync(runState.tokenSource.Token).AsTask() |> Async.AwaitTask 
                if runState.tokenSource.IsCancellationRequested |> not then 
                    runState.lastResponse.Value <- Some response
                    let outputText = RUtils.outputText response                       
                    runState.mailbox.Writer.TryWrite(ClientMsg.AppendOutput outputText) |> ignore
                    for o in response.output do
                        match o with
                        | Computer_call cb -> 
                            cb.pending_safety_checks |> List.map _.message |> String.concat "," |> shorten 100 |> postWarning runState
                            cb.action |> actionToString |> postAction runState
                            do! Async.Sleep 5000
                            do! doAction cb.action runState.browser                             
                        | _ -> ()
                    do! Async.Sleep(1000)
                if runState.tokenSource.IsCancellationRequested |> not then
                    do! sendNext runState
                    return! loop()      //done recursively to handle resumability better for human-in-the-loop in future
        }
        let comp = async {
            match! Async.Catch (loop()) with 
            | Choice1Of2 _ -> debug "dispose loop"
            | Choice2Of2 ex -> debug $"Error in loop: %s{ex.Message}"
        }
        Async.Start(comp,runState.tokenSource.Token)
        
        

    