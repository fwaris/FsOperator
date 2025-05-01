namespace FsOperator
open System
open FSharp.Control
open System.Threading
open System.Threading.Channels
open Microsoft.Playwright
open FsResponses
open System.IO

module ComputerUse =
  
    let startMessaging (runState:RunState) =
        let sendLoop = 
            runState.toModel.Reader.ReadAllAsync(runState.tokenSource.Token)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun request ->
                async {
                    runState.mailbox.Writer.TryWrite(ClientMsg.AppendLog $"--> {request}") |> ignore
                    let! response = Api.create request (Api.defaultClient()) |> Async.AwaitTask
                    runState.mailbox.Writer.TryWrite(ClientMsg.AppendLog $"<-- {response}") |> ignore
                    do! runState.fromModel.Writer.WriteAsync(response,runState.tokenSource.Token).AsTask() |> Async.AwaitTask
                }
            )
        let comp = 
            async {
                match! Async.Catch sendLoop with 
                | Choice1Of2 _ -> debug "dispose sendLoop"
                | Choice2Of2 ex -> debug $"Error in sendLoop: %s{ex.Message}"
            }
        Async.Start(comp, runState.tokenSource.Token)

    let snapshot (browser:IBrowser) = 
        async {
            let wctx = browser.Contexts.[0]
            let page = wctx.Pages.[0]
            let! image = page.ScreenshotAsync() |> Async.AwaitTask
            use ms = new MemoryStream(image)
            use bmp = System.Drawing.Image.FromStream(ms)
            let imgUrl = image |> RUtils.toImageUri
            return imgUrl,(int bmp.PhysicalDimension.Width, int bmp.PhysicalDimension.Height)
        }

    let sendStartMessage (runState:RunState) =
       async {
                let imgUrl,(w,h) = snapshot runState.browser |> Async.RunSynchronously
                let client = Api.defaultClient()
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

    let respnseIdsAndChecks (runState:RunState) = 
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
            match respnseIdsAndChecks runState with
            | None -> 
                debug "No response ids found"
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

    let mouseButton = function 
        | Buttons.Left -> MouseButton.Left
        | Buttons.Middle -> MouseButton.Middle
        | Buttons.Right -> MouseButton.Right
        | x -> failwith $"Unknown button {x}"
        
    let private (==) (a:string) (b:string) = a.Equals(b, StringComparison.OrdinalIgnoreCase)

    let doAction (action:Action) (browser:IBrowser)  =
        let task = 
            async {
                let page = browser.Contexts.[0].Pages.[0]
                match action with 
                | Click p -> 
                    debug $"Click %A{p}"
                    let opts = MouseClickOptions(Button = mouseButton p.button)
                    do! page.Mouse.ClickAsync(float32 p.x,float32 p.y, opts) |> Async.AwaitTask
                | Scroll p ->
                    debug $"Scroll %A{p}"
                    do! page.Mouse.MoveAsync(float32 p.x,float32 p.y) |> Async.AwaitTask                                
                    let! _ = page.EvaluateAsync($"window.scrollBy({p.scroll_x}, {p.scroll_y})")  |> Async.AwaitTask                                          
                    ()
                | Keypress p -> 
                    for k in p.keys do
                        debug $"Keypress {k}"
                        let mappedKey = 
                            if k == "Enter" then "Enter"                            
                            elif k == "space" then " "
                            elif k == "ESC" then "Escape"
                            elif k == "CTRL" then "Control"
                            else k
                        let opts = KeyboardPressOptions()
                        do! page.Keyboard.PressAsync(mappedKey, opts) |> Async.AwaitTask                            
                | Type p ->
                    debug $"Type %s{p.text}"
                    do! page.Keyboard.TypeAsync(p.text) |> Async.AwaitTask
                | Wait  -> 
                    debug "Wait"
                    do! Async.Sleep(2000)
                | Screenshot -> ()
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
                            match cb.pending_safety_checks with 
                            | [] -> ()
                            | scs -> for sc in scs do debug $"Pending safety check: %A{sc}"  //in future we may bring the human in loop to handle these warnings
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
        
        

    