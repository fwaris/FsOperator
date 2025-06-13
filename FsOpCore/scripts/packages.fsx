
#r "nuget: FSharp.Control.AsyncSeq"
#r "nuget: FSharp.SystemTextJson"
#r "nuget: Microsoft.Extensions.Configuration"
#r "nuget: Microsoft.Extensions.Configuration.Abstractions"
#r "nuget: Microsoft.Extensions.Hosting"
#r "nuget: Microsoft.Extensions.Logging"
#r "nuget: Microsoft.Extensions.Logging.Console" 
#r "nuget: SkiaSharp, 2.88.9" 
#r "nuget: System.Text.Json"
#r "nuget: Microsoft.Playwright"

#r @"E:\s\repos\FsOperator\RTOpenAI.Api\bin\Debug\net9.0-windows10.0.19041.0\RTOpenAI.Api.dll"

#load "../../FsResponses/Log.fs"
#load "../../FsResponses/FsResponses.fs"


#load "..\Misc\Constants.fs"
#load "..\Misc\Log.fs"
#load "..\Misc\Utils.fs"
#load "..\UIDrivers\UITypes.fs"
#load "..\UIDrivers\NativeDriver.fs" 
#load "..\UIDrivers\PlaywrightDriver.fs"
#load "..\Flows\Workflow.fs"
#load "..\Flows\Actions.fs"
#load "..\Flows\Chat.fs"
#load "..\Flows\FlowsCommon.fs"
#load "..\Flows\TaskFlow.fs"
