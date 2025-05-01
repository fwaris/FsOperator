namespace FsOperator

[<AutoOpen>]
module Utility = 

    let debug (msg:string) = 
        System.Diagnostics.Debug.WriteLine(msg)

    let shorten n (s:string) = if s.Length < n then s else s.Substring(0,n) + "\u2026"