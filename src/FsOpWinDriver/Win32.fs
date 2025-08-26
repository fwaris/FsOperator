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

    /// Resize and optionally move a window
    let resizeAndMoveWindow (hWnd: nativeint) (x: int) (y: int) (width: int) (height: int) =
        MoveWindow(hWnd, x, y, width, height, true)

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

    let getWindowAndScreenSizes (hWnd:IntPtr) =
       getWindowPos hWnd
       |> Option.bind (fun wSz ->
        getMonitorSizeForWindow hWnd 
        |> Option.map(fun mSz -> wSz,mSz))
