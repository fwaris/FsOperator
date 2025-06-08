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

    let SRCCOPY = 0x00CC0020; // BitBlt dwRop parameter

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

    let captureWindow (handle:IntPtr, isWindow:bool) =

            // fix windows 10 extra borders
            let adjustWindow = if isWindow then 7 else 0
            let hdcSrc = GetWindowDC(handle)
            let mutable windowRect = RECT()
            if GetWindowRect(handle,&windowRect) then 
                let width = windowRect.Right - windowRect.Left - adjustWindow * 2
                let height = windowRect.Bottom - windowRect.Top - adjustWindow
                let hdcDest = CreateCompatibleDC(hdcSrc)
                let hBitmap = CreateCompatibleBitmap(hdcSrc,width,height)
                let hOld = SelectObject(hdcDest,hBitmap)
                BitBlt(hdcDest,0,0,width,height,hdcSrc,0+adjustWindow,0,SRCCOPY) |> ignore
                SelectObject(hdcDest,hBitmap) |> ignore
                DeleteDC(hdcDest) |> ignore
                ReleaseDC(handle,hdcSrc) |> ignore
                let img = Image.FromHbitmap(hBitmap)
                DeleteObject(hBitmap) |> ignore
                Some img
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
