namespace  FsOperator

open PuppeteerSharp
open System.IO

module Browser = 

    let page () = 
        async {
            let! browser = Connection.connection() 
            let! pages = browser.PagesAsync() |> Async.AwaitTask     
            let page = pages.[0]
            for f in page.Frames do 
                debug $"frame: {f.Url} isMain {page.MainFrame.Url = f.Url}"
            debug "----"
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
            let! image = page.ScreenshotDataAsync() |> Async.AwaitTask
            use ms = new MemoryStream(image)
            use bmp = System.Drawing.Image.FromStream(ms)
            let imgUrl = FsResponses.RUtils.toImageUri image
            File.WriteAllBytes(@"e:\s\cua\screenshot.png", image)
            return imgUrl,(int bmp.PhysicalDimension.Width, int bmp.PhysicalDimension.Height)
        }

