namespace  FsOperator

open PuppeteerSharp
open System.IO
open SkiaSharp

module Browser =

    let page () =
        async {
            let! browser = Connection.connection()
            let! pages = browser.PagesAsync() |> Async.AwaitTask
            let page = pages |> Seq.toList |> List.rev |> List.head
            //for f in page.Frames do
            //    debug $"frame: {f.Url} isMain {page.MainFrame.Url = f.Url}"
            //debug "----"
            //let opts = WaitForNetworkIdleOptions()
            //opts.Timeout <- 1000
            //opts.IdleTime <- 200
            //do! page.WaitForNetworkIdleAsync() |> Async.AwaitTask
            return page
        }

    let snapshot() =
        async {
            let! page = page()

            let opts = ScreenshotOptions()
            opts.BurstMode <- true
            let! image = page.ScreenshotDataAsync() |> Async.AwaitTask
            do! page.SetBurstModeOffAsync() |> Async.AwaitTask
            let bmp = SKBitmap.Decode(image)
            let imgUrl = FsResponses.RUtils.toImageUri image
            File.WriteAllBytes(Path.Combine(homePath.Value, @"screenshot.png"), image)
            return imgUrl,(int bmp.Width, int bmp.Height)
        }

