
#load "packages.fsx"
open System.Net.Http.Headers
open System.Threading.Tasks
open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes
open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open System.Text.Json.Serialization
open FsResponses

let jsonObt = """
{
  "id": "resp_68120cfbf7348191bbd6a9f9edb77b4f0b27122f87039991",
  "object": "response",
  "created_at": 1746013436,
  "status": "completed",
  "error": null,
  "incomplete_details": null,
  "instructions": "Find how to best do async in F# from bing.com",
  "max_output_tokens": null,
  "model": "computer-use-preview-2025-03-11",
  "output": [
    {
      "id": "rs_68120cff59f881918b745e7c6502c5020b27122f87039991",
      "type": "reasoning",
      "summary": []
    },
    {
      "id": "cu_68120cffd07c8191bf7d024ffc366c7d0b27122f87039991",
      "type": "computer_call",
      "status": "completed",
      "action": {
        "type": "click",
        "button": "left",
        "x": 170,
        "y": 345
      },
      "call_id": "call_52E4XSBRAG6tXw4KmcC5TUt4",
      "pending_safety_checks": []
    }
  ],
  "parallel_tool_calls": false,
  "previous_response_id": null,
  "reasoning": {
    "effort": "medium",
    "generate_summary": null,
    "summary": null
  },
  "service_tier": "default",
  "store": false,
  "temperature": 1.0,
  "text": {
    "format": {
      "type": "text"
    }
  },
  "tool_choice": "auto",
  "tools": [
    {
      "type": "computer_use_preview",
      "display_height": 997,
      "display_width": 1212,
      "environment": "browser"
    }
  ],
  "top_p": 1.0,
  "truncation": "auto",
  "usage": {
    "input_tokens": 1358,
    "input_tokens_details": {
      "cached_tokens": 0
    },
    "output_tokens": 64,
    "output_tokens_details": {
      "reasoning_tokens": 0
    },
    "total_tokens": 1422
  },
  "user": null,
  "metadata": {}
}
"""


let test() =
    let respDser = JsonSerializer.Deserialize<Response>(jsonObt, options=Api.serOpts)


    let resp = Api.createWithDefaults "write a haiku about F#" |> runT

    let resp2 = 
        let client = Api.defaultClient()
        let cont = Input_text {|text = "List good F# learning resources" |}
        let input = { Message.Default with content=[cont]}
        let req = {Request.Default with input = [Message input]; tools=[Tool.DefaultWebSearch]}
        client |> Api.create req |> runT

    RUtils.outputText resp2 |> printfn "%s"

    let resp3 = 
        let image = File.ReadAllBytes(@"C:\Users\Faisa\Pictures\Screenshots\Screenshot 2024-09-20 061619.png") |> RUtils.toImageUri
        let client = Api.defaultClient()
        let cont = Input_text {|text = "Describe the image" |}
        let contImg = Input_image {|image_url = image|}
        let input = { Message.Default with content=[cont; contImg]}
        let req = {Request.Default with input = [Message input]}
        client |> Api.create req |> runT

    RUtils.outputText resp3 |> printfn "%s"

    let resp4  = 
        let image = File.ReadAllBytes(@"C:\Users\Faisa\Pictures\Screenshots\Bing.png") 
        let imUrl = image |> RUtils.toImageUri
        let ms = new MemoryStream(image)
        let i2 = System.Drawing.Image.FromStream(ms)
        let client = Api.defaultClient()
        let contImg = Input_image {|image_url = imUrl|}
        let input = { Message.Default with content=[contImg]}
        let tool = Tool_Computer_use {|display_height=int i2.PhysicalDimension.Height; display_width = int i2.PhysicalDimension.Width; environment = ComputerEnvironment.browser|}
        let req = {Request.Default with 
                    input = [Message input]; tools=[tool]; 
                    instructions= Some "Find how to best do async in F# from bing.com"; 
                    model=Models.computer_use_preview
                    truncation = Some "auto"
                }
        client |> Api.create req |> runT

    ()


