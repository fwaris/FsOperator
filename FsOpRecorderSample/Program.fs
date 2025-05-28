open System
open System.Threading.Tasks
open PuppeteerSharp

let START_URL = "https://www.google.com"

let recordUserActions () = task {
    let! dnldRstl  = (new BrowserFetcher()).DownloadAsync(BrowserTag.Stable) 
    dnldRstl.BuildId |> printfn "Chromium downloaded: %s"

    // Launch browser; PuppeteerSharp will handle Chromium download automatically
    let! browser = Puppeteer.LaunchAsync(LaunchOptions(Headless = false, Channel=BrowserData.ChromeReleaseChannel.Stable))
    let! page = browser.NewPageAsync()

    // Hook into console messages
    page.Console.Add(fun args ->
        printfn "[Browser Log] %s" args.Message.Text
    )

    // Navigate to a page
    let! _ = page.GoToAsync(START_URL)

    // Inject JavaScript to log interactions
    let script = """
        () => {
            document.addEventListener('click', (e) => {
                console.log(JSON.stringify({
                    event: 'click',
                    tag: e.target.tagName,
                    id: e.target.id,
                    className: e.target.className
                }));
            });

            document.addEventListener('input', (e) => {
                console.log(JSON.stringify({
                    event: 'input',
                    tag: e.target.tagName,
                    id: e.target.id,
                    value: e.target.value
                }));
            });

            document.addEventListener('keydown', (e) => {
                console.log(JSON.stringify({
                    event: 'keydown',
                    key: e.key
                }));
            });
        }
    """
    let! _ = page.EvaluateFunctionAsync(script)

    printfn "Recording... Press Enter to stop."
    Console.ReadLine() |> ignore

    do! browser.CloseAsync()
}

[<EntryPoint>]
let main _ =
    recordUserActions().GetAwaiter().GetResult()
    0
