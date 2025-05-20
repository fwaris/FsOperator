namespace FsOpMCPServer
open System
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

module Pgm = 

    [<EntryPoint>]
    let main args =
        let builder = Host.CreateApplicationBuilder(args)

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<WeatherTools>() 
        |> ignore

        builder.Logging.AddConsole(fun options ->
            options.LogToStandardErrorThreshold <- LogLevel.Trace
        ) |> ignore

        builder.Services.AddSingleton<HttpClient>(fun _ ->
            let client = new HttpClient(BaseAddress = Uri("https://api.weather.gov"))
            client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue("weather-tool", "1.0"))
            client
        ) |> ignore

        builder.Build().RunAsync() |> Async.AwaitTask |> Async.RunSynchronously
        0
