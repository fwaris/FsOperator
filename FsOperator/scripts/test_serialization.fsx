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

let jsonObt = """
{
  "id": "resp_67ccd3a9da748190baa7f1570fe91ac604becb25c45c1d41",
  "object": "response",
  "created_at": 1741476777,
  "status": "completed",
  "error": null,
  "incomplete_details": null,
  "instructions": null,
  "max_output_tokens": null,
  "model": "gpt-4o-2024-08-06",
  "output": [
    {
      "type": "message",
      "id": "msg_67ccd3acc8d48190a77525dc6de64b4104becb25c45c1d41",
      "status": "completed",
      "role": "assistant",
      "content": [
        {
          "type": "output_text",
          "text": "The image depicts a scenic landscape with a wooden boardwalk or pathway leading through lush, green grass under a blue sky with some clouds. The setting suggests a peaceful natural area, possibly a park or nature reserve. There are trees and shrubs in the background.",
          "annotations": []
        }
      ]
    }
  ],
  "parallel_tool_calls": true,
  "previous_response_id": null,
  "reasoning": {
    "effort": null,
    "summary": null
  },
  "store": true,
  "temperature": 1.0,
  "text": {
    "format": {
      "type": "text"
    }
  },
  "tool_choice": "auto",
  "tools": [],
  "top_p": 1.0,
  "truncation": "disabled",
  "usage": {
    "input_tokens": 328,
    "input_tokens_details": {
      "cached_tokens": 0
    },
    "output_tokens": 52,
    "output_tokens_details": {
      "reasoning_tokens": 0
    },
    "total_tokens": 380
  },
  "user": null,
  "metadata": {}
}
"""

type ResponseError = {
    code : string
    message : string
    ``type`` : string option
    param : string option
}

type ResponseErrorObj = {
    error : ResponseError
}

type IncompleteDetails = {
    reason : string
}

type Reasoning = {
  effort : string option
  summary : string option    
}

type TextOutputFormat = 
    | [<JsonName "text" >] Text
    | [<JsonName "json_schema" >] Json_schema of {| name : string ; schema : JsonElement; strict: bool |}

type TextOutput = {
    format : TextOutputFormat  
}

type User_Location = {
  ``type`` : string
  city : string option
  country : string option
  region : string option 
  timezone : string option
}

module SearchSizeContextSize = 
    let low = "low" 
    let medium = "medium"
    let high = "high"

module ComputerEnvironment = 
    let browser = "browser"
    let mac = "mac"
    let windows = "windows"
    let ubuntu = "ubuntu"

module Models = 
    let gpt_41 = "gpt-4.1"
    let computer_use_preview = "computer-use-preview"

type Tool = 
  | [<JsonName "file_search" >] Tool_File_search of {|vector_store_ids : string list; filters: JsonElement option; maximum_num_results: int option; ranking_options : JsonElement option|}
  | [<JsonName "function" >] Tool_Function of {|name:string; description:string; parameters:JsonElement; strict : bool|}  
  | [<JsonName "web_search_preview" >] Tool_Web_search of {|search_context_size : string; user_location : User_Location option|}
  | [<JsonName "computer_use_preview" >] Tool_Computer_use of {|display_height : int; display_width: int; environment:string;|}
with
    static member DefaultWebSearch = Tool_Web_search {|search_context_size=SearchSizeContextSize.medium; user_location = None|}

type OutputText = {
    text : string
    annotations : JsonElement option
}

type Content = 
  | [<JsonPropertyName "output_text">] Output_text of OutputText // {|text : string; annotations : JsonElement option|}
  | [<JsonPropertyName "input_text">] Input_text of {|text : string|}
  | [<JsonPropertyName "refusal">] Refusal of {|refusal:string;|}
  | [<JsonPropertyName "input_image">] Input_image of {|image_url:string|}
  
type Message = {
    id : string option
    status : string option
    role : string
    content : Content list
}
with static member Default = {
        id = None
        status = None
        role = "user"
        content = []
    }

type ResponseOutput = 
  | Message of Message
  | Image of {|image: string; annotations: JsonElement option|}
  | File of {|file: string; annotations: JsonElement option|}
  | Function_call of {|name:string; arguments:string|}
  | Web_search of {|search_context_size : string; user_location : User_Location option|}
  | [<JsonName "computer_use_preview" >] Computer_use of {|display_height : int; display_width: int; environment:string;|}

type Response = {
    id : string
    ``object`` : string
    created_at : int64
    status : string
    error : ResponseError option
    incomplete_details : IncompleteDetails option
    instructions : string option
    max_output_tokens : int option
    model : string
    output : ResponseOutput list
    parallel_tool_calls : bool
    previous_response_id : string option
    reasoning : Reasoning option
    store : bool
    temperature : float32
    text : TextOutput option
    tool_choice : string
    tools : Tool list
    top_p : float32
    truncation : string option //auto, disabled
    usage : JsonObject option
    user : string option
}

type Request = {
    model : string
    input : Message list
    instructions : string option
    max_output_tokens : int option
    metadata : Map<string,string> option
    parallel_tool_calls : bool
    previous_response_id : string option
    reasoning : Reasoning option
    service_tier : string //auto, default, flex
    store : bool
    stream : bool
    temperature : float32
    text : TextOutput option
    tool_choice : string //none; auto; required
    tools : Tool list
    top_p : float32
    truncation : string option //auto, disabled
    user : string option
}
    with static member Default = {
            model = "gpt-4.1"
            input = []
            instructions = None
            max_output_tokens = None
            metadata = None
            parallel_tool_calls = false
            previous_response_id = None
            reasoning = None
            service_tier = "auto"
            store = false
            stream = false
            temperature = 1.0f
            text = None
            tool_choice = "auto"
            tools = []
            top_p = 1.0f
            truncation = None
            user  = None
        }

let runT (t:Task<'t>) = t.Result

exception ApiError of ResponseErrorObj

module RUtils = 
    let outputText (resp:Response) = 
        [
            for r in resp.output do
            match r with 
            | Message m -> 
                for c in m.content do
                    match c with 
                    | Output_text t -> yield t.text
                    | _ -> ()
            | _ -> ()
        ]
        |> String.concat " "

    let toImageUri (bytes:byte[]) =
        let imageBytes = System.Convert.ToBase64String bytes
        $"data:image/jpeg;base64,{imageBytes}"

module service =
    let serOpts = 
        let opts = 
            JsonFSharpOptions.Default()
                .WithUnionInternalTag()
                .WithUnionTagName("type")
                .WithUnionUnwrapRecordCases()
                .WithUnionTagCaseInsensitive()        
                .ToJsonSerializerOptions()
        opts.WriteIndented <- true
        opts


    let newClient(key:string) = 
        let client = new HttpClient()
        client.BaseAddress <- Uri "https://api.openai.com/v1"
        client.DefaultRequestHeaders.Authorization <- new Headers.AuthenticationHeaderValue("Bearer",key)        
        client

    let defaultClient() = newClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))

    let create (req:Request) (client:#HttpClient) =         
        task {            
            let builder = UriBuilder(client.BaseAddress)
            builder.Path <- builder.Path + "/responses"
            let reqstr = JsonSerializer.Serialize(req,options=serOpts)
            printfn "%s" reqstr
            //use! resp = client.PostAsJsonAsync(builder.Uri, req,options=serOpts)
            use strContent = new StringContent(reqstr,MediaTypeHeaderValue("application/json"))
            use! resp = client.PostAsync(builder.Uri,strContent)
            if resp.StatusCode = Net.HttpStatusCode.OK || resp.StatusCode = Net.HttpStatusCode.Accepted then 
                let! str = resp.Content.ReadAsStringAsync()
                printfn "%A" str
                //return! resp.Content.ReadFromJsonAsync<Response>(options=serOpts)
                return JsonSerializer.Deserialize<Response>(str,options=serOpts)            
            else 
                let! str = resp.Content.ReadAsStringAsync()
                let err = 
                    try 
                        let err = JsonSerializer.Deserialize<ResponseErrorObj>(str,options=serOpts)
                        Some (ApiError err)
                    with ex -> 
                        None 
                match err with 
                | Some e -> return raise e
                | None  -> return failwith $"{str}"
        }

    let createWithDefaults (input:string) = 
        create 
            ({Request.Default with 
                input=[
                    {Message.Default with content=[Input_text {|text=input|}]}
                ]}) 
            (defaultClient())

let test() =
    let resp = service.createWithDefaults "write a haiku about F#" |> runT

    let resp2 = 
        let client = service.defaultClient()
        let cont = Input_text {|text = "List good F# learning resources" |}
        let input = { Message.Default with content=[cont]}
        let req = {Request.Default with input = [input]; tools=[Tool.DefaultWebSearch]}
        client |> service.create req |> runT

    RUtils.outputText resp2 |> printfn "%s"

    let resp3 = 
        let image = File.ReadAllBytes(@"C:\Users\Faisa\Pictures\Screenshots\Screenshot 2024-09-20 061619.png") |> RUtils.toImageUri
        let client = service.defaultClient()
        let cont = Input_text {|text = "Describe the image" |}
        let contImg = Input_image {|image_url = image|}
        let input = { Message.Default with content=[cont; contImg]}
        let req = {Request.Default with input = [input]}
        client |> service.create req |> runT

    RUtils.outputText resp3 |> printfn "%s"

    let resp4  = 
        let image = File.ReadAllBytes(@"C:\Users\Faisa\Pictures\Screenshots\Screenshot 2024-09-20 061619.png") 
        let imUrl = image |> RUtils.toImageUri
        let ms = new MemoryStream(image)
        let i2 = System.Drawing.Image.FromStream(ms)
        let client = service.defaultClient()
        let contImg = Input_image {|image_url = imUrl|}
        let input = { Message.Default with content=[contImg]}
        let tool = Tool_Computer_use {|display_height=int i2.PhysicalDimension.Height; display_width = int i2.PhysicalDimension.Width; environment = ComputerEnvironment.browser|}
        let req = {Request.Default with 
                    input = [input]; tools=[tool]; 
                    instructions= Some "drive the UI to get the answer for how to best use F# for async"; 
                    model=Models.computer_use_preview
                    truncation = Some "auto"
                }
        client |> service.create req |> runT

    ()


