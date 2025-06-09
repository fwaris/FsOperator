namespace FsOpWinDriver
open System
open System.IO
open System.Drawing
open System.Drawing.Imaging
open System.Diagnostics
open System.Runtime.InteropServices
open WindowsInput

module Win32 =
    type EnumWindowsProc = delegate of IntPtr * IntPtr -> bool

    // Define necessary Win32 structures and functions
    [<StructLayout(LayoutKind.Sequential)>]
    type RECT =
        struct
            val mutable Left: int
            val mutable Top: int
            val mutable Right: int
            val mutable Bottom: int
        end

    // Define the MONITORINFO structure
    [<StructLayout(LayoutKind.Sequential)>]
    type MONITORINFO =
        struct
            val mutable cbSize: uint32
            val mutable rcMonitor: RECT
            val mutable rcWork: RECT
            val mutable dwFlags: uint32
        end

    [<DllImport("user32.dll")>]
    extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam)

    [<DllImport("user32.dll")>]
    extern bool IsWindowVisible(IntPtr hWnd)

    [<DllImport("user32.dll")>]
    extern uint32 GetWindowThreadProcessId(IntPtr hWnd, uint32& lpdwProcessId)

    [<DllImport("user32.dll")>]
    extern IntPtr GetWindowDC(IntPtr hWnd)

    [<DllImport("user32.dll")>]
    extern int ReleaseDC(IntPtr hWnd, IntPtr hDC)

    [<DllImport("gdi32.dll")>]
    extern IntPtr CreateCompatibleDC(IntPtr hDC)

    [<DllImport("gdi32.dll")>]
    extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight)

    [<DllImport("gdi32.dll")>]
    extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject)

    [<DllImport("gdi32.dll")>]
    extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
                       IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop)

    [<DllImport("gdi32.dll")>]
    extern bool DeleteDC(IntPtr hDC)

    [<DllImport("gdi32.dll")>]
    extern bool DeleteObject(IntPtr hObject)

    [<DllImport("user32.dll")>]
    extern bool GetWindowRect(IntPtr hWnd, RECT& lpRect)

    [<DllImport("user32.dll")>]
    extern IntPtr GetDC(IntPtr hWnd)

    [<DllImport("user32.dll")>]
    extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags)

    // Import the MonitorFromWindow function
    [<DllImport("user32.dll")>]
    extern IntPtr MonitorFromWindow(IntPtr hwnd, uint32 dwFlags)

    // Import the GetMonitorInfo function
    [<DllImport("user32.dll", CharSet = CharSet.Auto)>]
    extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO& lpmi)

    [<DllImport("user32.dll")>]
    extern bool UpdateWindow(nativeint hWnd)

    [<DllImport("user32.dll")>]
    extern bool MoveWindow(
        nativeint hWnd,
        int X, int Y,
        int nWidth, int nHeight,
        bool bRepaint
    )


    // Constant for MonitorFromWindow
    let MONITOR_DEFAULTTONEAREST = 0x00000002u

    let SRCCOPY = 0x00CC0020; // BitBlt dwRop parameter


    [<DllImport("user32.dll")>]
    extern bool SetForegroundWindow(nativeint hWnd)

    let findTopmostWindow (targetPid: int) : IntPtr option =
        let mutable result = IntPtr.Zero
        let callback = EnumWindowsProc(fun hWnd _ ->
            let mutable pid = 0u
            GetWindowThreadProcessId(hWnd, &pid) |> ignore
            if pid = uint32 targetPid && IsWindowVisible(hWnd) then
                result <- hWnd
                false // Stop enumeration
            else
                true  // Continue enumeration
        )
        EnumWindows(callback, IntPtr.Zero) |> ignore
        if result <> IntPtr.Zero then Some result else None

    let captureWindowPixel (hwnd: IntPtr) =
        let mutable rect = RECT()
        GetWindowRect(hwnd, &rect) |> ignore
        let width = rect.Right - rect.Left
        let height = rect.Bottom - rect.Top
        let bmp = new Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)
        let gfxBmp = Graphics.FromImage(bmp)
        let hdcBitmap = gfxBmp.GetHdc()
        if not (PrintWindow(hwnd, hdcBitmap, 0u)) then
            let error = Marshal.GetLastWin32Error()
            let exn = new System.ComponentModel.Win32Exception(error)
            printfn "ERROR: %d: %s" error exn.Message
        gfxBmp.ReleaseHdc(hdcBitmap)
        gfxBmp.Dispose()
        Some bmp


    /// Resize and optionally move a window
    let resizeAndMoveWindow (hWnd: nativeint) (x: int) (y: int) (width: int) (height: int) =
        MoveWindow(hWnd, x, y, width, height, true)


    let captureWindowPrint (hWnd: nativeint) : Bitmap option =
        let mutable rect = RECT()
        if GetWindowRect(hWnd, &rect) then
            let width = rect.Right - rect.Left
            let height = rect.Bottom - rect.Top
            let hdcWindow = GetWindowDC(hWnd)
            let hdcMemDC = CreateCompatibleDC(hdcWindow)
            let hBitmap = CreateCompatibleBitmap(hdcWindow, width, height)
            let hOld = SelectObject(hdcMemDC, hBitmap)
        
            let succeeded = PrintWindow(hWnd, hdcMemDC, 0u)
        
            let img = if succeeded then Some(Image.FromHbitmap(hBitmap)) else None

            SelectObject(hdcMemDC, hOld) |> ignore
            DeleteDC(hdcMemDC) |> ignore
            ReleaseDC(hWnd, hdcWindow) |> ignore
            DeleteObject(hBitmap) |> ignore

            img
        else
            None

    let captureWindow (handle:IntPtr) =
            // fix windows 10 extra borders
            let mutable windowRect = RECT()
            if GetWindowRect(handle,&windowRect) then 
                let width = windowRect.Right - windowRect.Left
                let height = windowRect.Bottom - windowRect.Top
                //let hdcSrc = GetWindowDC(handle)
                let hdcScreen = GetDC(IntPtr.Zero)
                let hdcMemDC = CreateCompatibleDC(hdcScreen)
                let hBitmap = CreateCompatibleBitmap(hdcScreen, width, height)
                let hOld = SelectObject(hdcMemDC, hBitmap)
                BitBlt(hdcMemDC,0,0,width,height,hdcScreen,windowRect.Left,windowRect.Top,SRCCOPY) |> ignore

                //BitBlt(hdcMemDC, 0, 0, captureWidth, captureHeight, hdcScreen, adjustedLeft, adjustedTop, SRCCOPY) |> ignore
                use bmp = Image.FromHbitmap(hBitmap)
                // Cleanup
                SelectObject(hdcMemDC, hOld) |> ignore
                DeleteObject(hBitmap) |> ignore
                DeleteDC(hdcMemDC) |> ignore
                ReleaseDC(IntPtr.Zero, hdcScreen) |> ignore
                Some (new Bitmap(bmp))
            else
                None

    let getWindowPos hWnd = 
        let mutable windowRect = RECT()
        if GetWindowRect(hWnd,&windowRect) then 
            let width = windowRect.Right - windowRect.Left
            let height = windowRect.Bottom - windowRect.Top
            Some (windowRect.Left, windowRect.Top, width,height)
        else 
            None

    let captureWindowViewport viewport (handle:IntPtr) =
            let vpW,vpH = viewport
            // fix windows 10 extra borders
            match getWindowPos handle with 
            | Some (x,y,width,height) -> 
                let hdcScreen = GetDC(IntPtr.Zero)
                let hdcMemDC = CreateCompatibleDC(hdcScreen)
                let hBitmap = CreateCompatibleBitmap(hdcScreen, vpW, vpH)
                let hOld = SelectObject(hdcMemDC, hBitmap)
                let copyW = min vpW width
                let copyH = min vpH height
                BitBlt(hdcMemDC,0,0,copyW,copyH,hdcScreen,x,y,SRCCOPY) |> ignore
                use bmp = Image.FromHbitmap(hBitmap)
                // Cleanup
                SelectObject(hdcMemDC, hOld) |> ignore
                DeleteObject(hBitmap) |> ignore
                DeleteDC(hdcMemDC) |> ignore
                ReleaseDC(IntPtr.Zero, hdcScreen) |> ignore
                Some (new Bitmap(bmp))
            | None -> None


    let captureWindowP (handle: IntPtr, isWindow: bool) =
        let adjustWindow = if isWindow then 7 else 0
        let hdcSrc = GetWindowDC(handle)
        let mutable windowRect = RECT()
        if GetWindowRect(handle, &windowRect) then
            let width = windowRect.Right - windowRect.Left - adjustWindow * 2
            let height = windowRect.Bottom - windowRect.Top - adjustWindow
            let hdcDest = CreateCompatibleDC(hdcSrc)
            let hBitmap = CreateCompatibleBitmap(hdcSrc, width, height)
            let hOld = SelectObject(hdcDest, hBitmap)
        
            let success = PrintWindow(handle, hdcDest, 0u)
        
            SelectObject(hdcDest, hOld) |> ignore
            DeleteDC(hdcDest) |> ignore
            ReleaseDC(handle, hdcSrc) |> ignore
        
            if success then
                let img = Image.FromHbitmap(hBitmap)
                DeleteObject(hBitmap) |> ignore
                Some img
            else
                DeleteObject(hBitmap) |> ignore
                None
        else
            None
(*
            // get te hDC of the target window
            IntPtr hdcSrc = User32.GetWindowDC(handle);
            // get the size
            User32.RECT windowRect = new User32.RECT();
            User32.GetWindowRect(handle, ref windowRect);
            // create a device context we can copy to
            IntPtr hdcDest = GDI32.CreateCompatibleDC(hdcSrc);
            // create a bitmap we can copy it to,
            // using GetDeviceCaps to get the width/height
            IntPtr hBitmap = GDI32.CreateCompatibleBitmap(hdcSrc, width, height);
            // select the bitmap object
            IntPtr hOld = GDI32.SelectObject(hdcDest, hBitmap);
            // bitblt over
            GDI32.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0 + adjustWindow, 0, GDI32.SRCCOPY);
            // restore selection
            GDI32.SelectObject(hdcDest, hOld);
            // clean up
            GDI32.DeleteDC(hdcDest);
            User32.ReleaseDC(handle, hdcSrc);
            // get a .NET image object for it
            Image img = Image.FromHbitmap(hBitmap);
            // free up the Bitmap object
            GDI32.DeleteObject(hBitmap);
            return img;
        }

*)
    // Function to get the monitor size for a given window handle
    let getMonitorSizeForWindow (hwnd: IntPtr) =
        let hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)
        if hMonitor = IntPtr.Zero then
            None
        else
            let mutable mi = MONITORINFO(cbSize = uint32 (Marshal.SizeOf(typeof<MONITORINFO>)))
            if GetMonitorInfo(hMonitor, &mi) then
                let width = mi.rcMonitor.Right - mi.rcMonitor.Left
                let height = mi.rcMonitor.Bottom - mi.rcMonitor.Top
                Some (width, height)
            else
                None

    let captureWindowScreenshot (hWnd: IntPtr) : Bitmap option =
        let mutable rect = RECT()
        if GetWindowRect(hWnd, &rect) then
            let windowWidth = rect.Right - rect.Left
            let windowHeight = rect.Bottom - rect.Top
            let windowCenterX = rect.Left + windowWidth / 2
            let windowTop = rect.Top

            // Desired capture size
            let captureWidth = 1280
            let captureHeight = 768

            // Calculate top-left corner of the capture region
            let captureLeft = windowCenterX - captureWidth / 2
            let captureTop = windowTop

            let sz = getMonitorSizeForWindow(hWnd)
            let screenWidth, screenHeight = sz |> Option.defaultValue (windowWidth,windowHeight)

            let adjustedLeft = Math.Max(0, Math.Min(captureLeft, screenWidth - captureWidth))
            let adjustedTop = Math.Max(0, Math.Min(captureTop, screenHeight - captureHeight))

            // Create bitmap and perform BitBlt
            let hdcScreen = GetDC(IntPtr.Zero)
            let hdcMemDC = CreateCompatibleDC(hdcScreen)
            let hBitmap = CreateCompatibleBitmap(hdcScreen, captureWidth, captureHeight)
            let hOld = SelectObject(hdcMemDC, hBitmap)
            BitBlt(hdcMemDC, 0, 0, captureWidth, captureHeight, hdcScreen, adjustedLeft, adjustedTop, SRCCOPY) |> ignore
            let bmp = Image.FromHbitmap(hBitmap)
            // Cleanup
            SelectObject(hdcMemDC, hOld) |> ignore
            DeleteObject(hBitmap) |> ignore
            DeleteDC(hdcMemDC) |> ignore
            ReleaseDC(IntPtr.Zero, hdcScreen) |> ignore
            Some (new Bitmap(bmp))
        else
            None
