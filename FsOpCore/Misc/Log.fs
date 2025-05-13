namespace FsOpCore

open Microsoft.Extensions.Logging

type FsOperatorLog() = class end 
module Log =     
        let getLogger() : ILogger<FsOperatorLog>  = 
            LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<FsOperatorLog>()

        let private _log= lazy(getLogger())
        let info  (msg:string) = if _log.Value <> Unchecked.defaultof<_> then _log.Value.LogInformation(msg)
        let warn (msg:string) = if _log.Value <> Unchecked.defaultof<_> then _log.Value.LogWarning(msg)
        let error (msg:string) = if _log.Value <> Unchecked.defaultof<_> then _log.Value.LogError(msg)
        let exn (exn:exn,msg) = if _log.Value <> Unchecked.defaultof<_> then _log.Value.LogError(exn,msg)
        
