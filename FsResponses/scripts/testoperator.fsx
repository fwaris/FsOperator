#load "packages.fsx"
open System
open Microsoft.Playwright
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.SemanticKernel
open Microsoft.Extensions.AI
open System.Text.Json.Nodes

let MODEL = "gpt-4.1"


let playwright =  Playwright.CreateAsync().Result
let brower = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless=false)).Result
let page = brower.NewPageAsync().Result
page.SetViewportSizeAsync(1024,768)
let r = page.GotoAsync("https://playwright.dev/dotnet").Result;
let sc = page.ScreenshotAsync().Result


let sch = AIJsonUtilities.CreateJsonSchema(typeof<ResponsesApi.ImageUrl>)
