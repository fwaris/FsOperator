namespace FsOpCore
open System
open System.IO
open System.Runtime.InteropServices

[<AutoOpen>]
module Utility =

    
    let getApiKey() = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
  

    let homePath = lazy(
        match Environment.OSVersion.Platform with 
        | PlatformID.Unix 
        | PlatformID.MacOSX -> Environment.GetEnvironmentVariable("HOME") 
        | _                 -> Environment.GetEnvironmentVariable("USERPROFILE"))

    let debug (msg:string) = 
        System.Diagnostics.Debug.WriteLine(msg)

    let shorten n (s:string) = 
        if s.Length < n then 
            s 
        else
            let left = s.Substring(0,n/2)
            let right = s.Substring(s.Length - n/2)
            left + " [\u2026] " + right

    let isEmpty (s:string) = 
        String.IsNullOrWhiteSpace s

    let fixEmpty s = if isEmpty s then "" else s
    
    let newId() = 
        Guid.NewGuid().ToByteArray() 
        |> Convert.ToBase64String 
        |> Seq.takeWhile (fun c -> c <> '=') 
        |> Seq.map (function '/' -> 'a' | c -> c)
        |> Seq.toArray 
        |> String

    let (@@) (a:string) (b:string) = Path.Combine(a,b)
    
    /// String comparison that ignores case
    let (=*=) (a:string) (b:string) = a.Equals(b, StringComparison.OrdinalIgnoreCase)

    let isWindows() = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    let isMac() = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

    let ddict xs = System.Collections.Generic.Dictionary(dict xs)