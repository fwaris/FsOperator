namespace FsOpWinDriver
open System
open System.IO
open System.Drawing.Imaging
open WindowsInput
open System.Management

module WDriver =

    let getProcId (processName:string) (arg:string option) =    
        let query = $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{processName}'"
        use searcher = new ManagementObjectSearcher(query)
        searcher.Get()
        |> Seq.cast<ManagementObject>
        |> Seq.choose (fun x -> 
            let commandLine = x.["CommandLine"] |> string
            printfn "%s" commandLine
            let id =  x.["ProcessId"] |> string |> int
            match arg with 
            | Some arg when commandLine.Contains(arg) -> Some id
            | Some arg                                -> None
            | None                                    -> Some id)
        |> Seq.tryHead
        |> Option.defaultWith (fun _ -> failwith $"no process found match criteria '{processName}' and command line filter='{arg}'")

    let getPid name = 
        let procs = System.Diagnostics.Process.GetProcessesByName(name)
        if procs.Length = 0 then    
            failwith $"no process found with name '{name}'"
        procs.[0].Id    

    let viewport = 1280,768

    let snapshot (name:string) (arg:string option) = async {
        let name = if not (name.EndsWith(".exe")) then name + ".exe" else name
        let pid = getProcId name arg
        let handle = Win32.findTopmostWindow pid 
        handle 
        |> Option.bind Win32.getWindowAndScreenSizes
        |> Option.iter (fun ((x,y,w,h),(mW,mH)) ->
            let width,height = viewport
            let margin = 100
            let x' = if x + width + margin > mW then mW - width - margin else x
            let y' = if y + height + margin > mH then mH - height - margin else y
            Win32.resizeAndMoveWindow handle.Value x' y' width height |> ignore)
        do! Async.Sleep 0
        handle 
        |> Option.iter (fun h ->
            Win32.SetForegroundWindow(h) |> ignore
            Win32.UpdateWindow(h) |> ignore)
        do! Async.Sleep 100        
        let bitmap = 
            handle 
            |> Option.bind (fun h -> Win32.captureWindowViewport viewport (h)) 
            |> Option.defaultWith (fun _ -> failwith "unable to capture snapshot")
        use ms = new MemoryStream()
        bitmap.Save(ms, ImageFormat.Png)
        let buff = ms.GetBuffer()
        return buff, handle.Value
    }

    let translate (hWndRef:Ref<IntPtr>) (wX,wY) = 
        Win32.getWindowAndScreenSizes hWndRef.Value
        |> Option.map (fun ((x,y,w,h),(mW,mH)) -> x + wX, y + wY)
        |> Option.defaultWith (fun _ -> failwith "unable to locate window")
    
    let doubleClick hWndRef (x,y) = async {
        let x,y = translate hWndRef (x,y)
        let! v = 
            Simulate
                .Events()
                .MoveTo(x,y)
                .DoubleClick(Events.ButtonCode.Left)
                .Invoke() 
                |> Async.AwaitTask
        ()        
    }

    let click hWndRef (x,y,btn:Events.ButtonCode) = async {
        let x,y = translate hWndRef (x,y)
        let! v = 
            Simulate
                .Events()
                .MoveTo(x,y)
                .Click(btn)  
                .Invoke()
                |> Async.AwaitTask
                
        ()        
    }

    let typeText (text:string) = async {        
        let! v = 
            Simulate
                .Events()
                .Click(text)
                .Invoke() 
                |> Async.AwaitTask
        ()
    }

    let wheel (deltaX:int,deltaY:int) = async {
        let! v = 
            Simulate
                .Events()
                .Scroll(Events.ButtonCode.HScroll,deltaX)
                .Scroll(Events.ButtonCode.VScroll,deltaY)
                .Invoke()
                |> Async.AwaitTask
        ()
    }

    let move hWndRef (x:int,y:int) = async {
        let x,y = translate hWndRef (x,y)
        let! v = 
            Simulate
                .Events()
                .MoveTo(x,y)
                .Invoke()
                |> Async.AwaitTask
        ()
    }

    let scroll hWndRef (x:int, y:int) (scrollX:int, scrollY:int) = async {
        let x,y = translate hWndRef (x,y)
        let! v = 
            Simulate
                .Events()
                .MoveTo(x,y)
                .Scroll(Events.ButtonCode.HScroll, scrollX)
                .Scroll(Events.ButtonCode.VScroll, scrollY)
                .Invoke()
                |> Async.AwaitTask
        ()
    }

    let pressKeys (ks:Events.KeyCode[]) = async {
        let! v = 
            Simulate
                .Events()
                .Click(ks)
                .Invoke()
                |> Async.AwaitTask
        ()
    }

    let dragAndDrop hWndRef (sX:int,sY:int) (tX:int, tY:int) = async {
        let sX,sY = translate hWndRef (sX,sY)
        let tX,tY = translate hWndRef (tX,tY)
        let! v =
            let src = Events.MouseMove.Create(sX,sY,Events.MouseOffset.Absolute)
            let tgt = Events.MouseMove.Create(tX,tY,Events.MouseOffset.Absolute)
            Simulate
                .Events()
                .DragDrop(src,tgt)
                .Invoke()
                |> Async.AwaitTask
        ()
    }
 