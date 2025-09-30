open System
open System.Threading

typeof<WaitHandle>.GetMethods() 
|> Array.filter (fun m -> m.Name.Contains("Wait"))
|> Array.sortBy (fun m -> m.Name)
|> Array.iter (fun m -> printfn "%s : %s" m.Name (m.ToString()))
