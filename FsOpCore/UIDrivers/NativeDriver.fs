namespace FsOpCore
open System
open System.IO
open SkiaSharp
open System.Diagnostics

#if WINDOWS 
open System
open System.Runtime.InteropServices

module CmdSplit =
    [<DllImport("shell32.dll", SetLastError=true)>]
    extern IntPtr CommandLineToArgvW(
        [<MarshalAs(UnmanagedType.LPWStr)>] string cmdLine, int& argc)

    let split (cmdLine: string) : string[] =
        let mutable argc = 0
        let argv = CommandLineToArgvW(cmdLine, &argc)
        if argv = IntPtr.Zero then raise (new System.ComponentModel.Win32Exception())
        try
            Array.init argc (fun i ->
                let ptr = Marshal.ReadIntPtr(argv, i * IntPtr.Size)
                Marshal.PtrToStringUni(ptr))
        finally
            Marshal.FreeHGlobal(argv)

#endif 

module NativeDriver = 
#if WINDOWS 
    open FsOpWinDriver
    open WindowsInput.Events
    let winMapButton = function
        | MouseButton.Left -> ButtonCode.Left
        | MouseButton.Right -> ButtonCode.Right
        | MouseButton.Middle -> ButtonCode.Middle

    let winMapKeys (ks:string list) = 
        let ks = DriverUtils.canonicalize ks
        ks
        |> List.map (function 
            | K.Enter -> KeyCode.Enter
            | K.Backspace -> KeyCode.Backspace
            | K.Escape -> KeyCode.Escape
            | K.Shift -> KeyCode.Shift
            | K.Control -> KeyCode.Control
            | K.Tab -> KeyCode.Tab
            | K.ArrowLeft -> KeyCode.Left
            | K.ArrowRight -> KeyCode.Right
            | K.ArrowUp -> KeyCode.Up
            | K.ArrowDown -> KeyCode.Down
            | K.Alt -> KeyCode.Alt
            | K.AltGraph -> KeyCode.RAlt
            | K.Meta -> KeyCode.LWin
            | K.PageUp -> KeyCode.PageUp
            | K.PageDown -> KeyCode.PageDown
            | K.Home -> KeyCode.Home
            | K.End -> KeyCode.End
            | K.Insert -> KeyCode.Insert
            | K.Delete -> KeyCode.Delete
            | k -> k |> int |> uint16 |> box :?> WindowsInput.Events.KeyCode
        )
        |> List.toArray

    let winSnapshot (hWndRef:Ref<IntPtr>) (name:string) arg = async { 
        Log.info $"taking snapshot of window"
        let! bytes,hWnd = WDriver.snapshot name arg
        hWndRef.Value <- hWnd
        use ms = new MemoryStream(bytes)
        Log.info $"done native snapshot"
        let bmp = SKBitmap.Decode(ms)       
        let imgUrl = FsResponses.RUtils.toImageUri bytes
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(homePath.Value, @"screenshot.png"), bytes)
        return imgUrl,(bmp.Width, bmp.Height)
    }

    let startProcess (cmd:string) = async {
        let xs = CmdSplit.split cmd
        let exe,args = if xs.Length > 0 then xs.[0], Some (xs.[1]) else cmd,None
        let pi = ProcessStartInfo()
        pi.FileName <- exe
        match args with Some args -> pi.Arguments <- args | _ -> ()
        let p = new Process()
        p.StartInfo <- pi
        if p.Start() then 
            Log.info $"Process start '{cmd}', pid = {p.Id}"
       else 
            Log.error $"Process start '{cmd}'"
    }

#endif

    let create (name:string) (arg:string option) = 
#if WINDOWS 
        let hWndRef : Ref<IntPtr> = ref IntPtr.Zero
        let userInteraction =            
            {new IUIDriver with
                member _.doubleClick(x,y) = WDriver.doubleClick hWndRef (x,y)
                member _.click(x,y,btn) = WDriver.click hWndRef (x,y,winMapButton btn)
                member _.wheel(x,y) = WDriver.wheel(x,y)
                member _.move(x,y) = WDriver.move hWndRef (x,y)
                member _.scroll (x,y) (scrollX,scrollY) = WDriver.scroll hWndRef (x,y) (scrollX,scrollY)
                member _.pressKeys keys = WDriver.pressKeys (winMapKeys keys)
                member _.dragDrop (sX,sY) (tX,tY) = WDriver.dragAndDrop hWndRef (sX,sY) (tX,tY)
                member _.snapshot() = winSnapshot hWndRef name arg
                member _.goBack () = WDriver.pressKeys [|KeyCode.Alt; KeyCode.Left|] //by convention
                member _.goForward () = WDriver.pressKeys [|KeyCode.Alt; KeyCode.Right|]
                member _.typeText text = WDriver.typeText text
                member _.url () = async{ return None}
                member _.environment with get (): string = FsResponses.ComputerEnvironment.windows
                member _.start cmd = startProcess cmd
            }
        Na {|driver=userInteraction; processName = name; arg=arg|}        

#else
        failwith "native ui driver not implmented for non-windows platform"
#endif
