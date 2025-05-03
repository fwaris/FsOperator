namespace FsOperator

[<AutoOpen>]
module Utility = 

    let debug (msg:string) = 
        System.Diagnostics.Debug.WriteLine(msg)

    let shorten n (s:string) = 
        if s.Length < n then 
            s 
        else
            let left = s.Substring(0,n/2)
            let right = s.Substring(s.Length - n/2)
            left + "[\u2026]" + right