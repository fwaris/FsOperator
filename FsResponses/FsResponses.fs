namespace FsResponses
open System
open System.Net.Http.Headers
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Net.Http

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

[<JsonFSharpConverter(SkippableOptionFields=SkippableOptionFields.Always)>]
type Reasoning = {
  effort : string option
  summary : string option    
  generate_summary : string option
}
with 
    static member Default = {
        effort = None
        summary = None
        generate_summary = None
    }
    static member Medium = "medium"
    static member High = "high"
    static member Low = "low"


type TextOutputFormat = 
    | [<JsonName "text" >] Text
    | [<JsonName "json_schema" >] Json_schema of {| name : string ; schema : JsonNode; strict: bool |}

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

module Truncation = 
    let auto = "auto"
    let disabled = "disabled"

module Models = 
    let gpt_41 = "gpt-4.1"
    let computer_use_preview = "computer-use-preview"

module Buttons =
    let [<Literal>] Left = "left"
    let [<Literal>] Right = "right"
    let [<Literal>] Middle = "middle"

type Property =
    {
        ``type``: string
        description: string option
    }    

type Parameters =
    {
        ``type``: string
        properties: Map<string, Property>
        required: string list
    }
    static member Default = { ``type`` = "object"; properties = Map.empty; required = [] }

type Function =
    {
        name: string
        description: string
        parameters: Parameters
        strict : bool
    }
    static member Default = {name = ""; description = ""; parameters = Parameters.Default; strict=true}

type Tool = 
  | [<JsonName "file_search" >] Tool_File_search of {|vector_store_ids : string list; filters: JsonElement option; maximum_num_results: int option; ranking_options : JsonElement option|}
  | [<JsonName "function" >] Tool_Function of Function
  | [<JsonName "web_search_preview" >] Tool_Web_search of {|search_context_size : string; user_location : User_Location option|}
  | [<JsonName "computer_use_preview" >] Tool_Computer_use of {|display_height : int; display_width: int; environment:string;|}
with
    static member DefaultWebSearch = Tool_Web_search {|search_context_size=SearchSizeContextSize.medium; user_location = None|}

type OutputText = {
    text : string
    annotations : JsonElement option
}

type Content = 
  | [<JsonName "output_text">] Output_text of OutputText // {|text : string; annotations : JsonElement option|}
  | [<JsonName "input_text">] Input_text of {|text : string|}
  | [<JsonName "refusal">] Refusal of {|refusal:string;|}
  | [<JsonName "input_image">] Input_image of {|image_url:string|}
  
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

type SafetyCheck = {
    id : string
    code : string
    message : string
}

type OutputDetail = 
    | [<JsonPropertyName "input_image">] Computer_screenshot of {|image_url:string|}
    | [<JsonPropertyName "not_used">] DoNotUse of {|text:string|} //this is only to make this a multi-case union so that serializaton adds the type tag

[<JsonFSharpConverter(SkippableOptionFields=SkippableOptionFields.Always)>]
type ComputerCallOutput = {
    call_id : string
    acknowledged_safety_checks : SafetyCheck list    
    output : OutputDetail
    current_url : string option
}

type ReasoningSummary = {text:string; ``type``: string}

[<JsonFSharpConverter(SkippableOptionFields=SkippableOptionFields.Always)>]
type ReasoningOutput = {
    id : string
    summary : ReasoningSummary list
    status : string option
}

type Point = {x:int; y:int}
type Path = {
    path : Point list
} 

type Action = 
    | [<JsonName "click">] Click of {| button:string; x:int; y:int|}
    | [<JsonName "scroll">] Scroll of {|x:int; y:int; scroll_x:int; scroll_y:int|}
    | [<JsonName "keypress">] Keypress of {| keys:string list;|} //ctrl, alt, shift
    | [<JsonName "type">] Type of {| text:string|}
    | [<JsonName "wait">] Wait 
    | [<JsonName "screenshot">] Screenshot 
    | [<JsonName "double_click">] Double_click of {|x:int; y:int|}
    | [<JsonName "drag">] Drag of Path
    | [<JsonName "move">] Move of {| x:int; y:int |}

type ComputerCall = {
    id : string
    status : string
    action : Action
    call_id : string
    pending_safety_checks : SafetyCheck list
}

type FunctionCall = {
    id : string
    call_id : string
    name : string
    arguments: string
}

type FunctionCallOutput = {
    call_id : string
    output  : string    
}

[<RequireQualifiedAccess>]
type IOitem = 
  | [<JsonName "message" >] Message of Message
  | [<JsonName "image" >] Image of {|image: string; annotations: JsonElement option|}
  | [<JsonName "file" >] File of {|file: string; annotations: JsonElement option|}
  | [<JsonName "function_call" >] Function_call of FunctionCall
  | [<JsonName "function_call_output" >] Function_call_output of FunctionCallOutput
  | [<JsonName "web_search" >] Web_search of {|search_context_size : string; user_location : User_Location option|}
  | [<JsonName "computer_use_preview" >] Computer_use of {|display_height : int; display_width: int; environment:string;|}
  | [<JsonName "reasoning" >] Reasoning of ReasoningOutput
  | [<JsonName "computer_call" >] Computer_call of ComputerCall
  | [<JsonName "computer_call_output">] Computer_call_output of ComputerCallOutput

type Request = {
    model : string
    input : IOitem list
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
    metadata : Map<string,string> option
    output : IOitem list
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

exception ApiError of ResponseErrorObj

module RUtils =
    open System.Text.Json.Schema
    let private shortenN (s:string) n = if s.Length < n then s else s.Substring(0,n) + "\u2026"
    let private shorten (s:string) = shortenN s 100

    ///Convert a type to JsonSchema and package it as `structured format`.
    ///Use a simple type structure for reliablilty
    let structuredFormat (t:Type) = 
        let opts = JsonSerializerOptions.Default       
        let schema = opts.GetJsonSchemaAsNode(t)  
        match schema with 
        | :? JsonObject as j -> j.["type"] <- "object"
        | _ -> ()
        {format = Json_schema {|name=t.Name; schema=schema; strict=true|}}

    let parseContent<'t> (resp:Response) =        
        resp.output 
        |> List.tryPick(function IOitem.Message m -> Some m | _ -> None)
        |> Option.bind(fun m -> 
            m.content 
            |> List.tryPick (function 
                | Content.Refusal r -> Some (Choice2Of2 r.refusal)
                | Content.Output_text otxt ->
                    let t:'t = JsonSerializer.Deserialize<'t>(otxt.text)
                    Some(Choice1Of2 t)
                | _ -> None))

    let trimScreenshot (cco:OutputDetail) =
        match cco with 
        | Computer_screenshot i -> Computer_screenshot {|image_url=shortenN i.image_url 20|}
        | x -> x
    
    let trimImage = function
        | IOitem.Computer_call_output cco -> IOitem.Computer_call_output {cco with output = trimScreenshot cco.output}
        | IOitem.Image i ->  IOitem.Image {|i with image = shorten i.image |}
        | x -> x
        
    ///trim the large image base64 encoded string (to reduce log sizes)
    let trimResponse (resp:Response) =
        {resp with output = resp.output |> List.map trimImage}
        
    ///trim the large image base64 encoded string (to reduce log sizes)
    let trimRequest (req:Request) =
        {req with input = req.input |> List.map trimImage}
        
    let outputText (resp:Response) = 
        [
            for r in resp.output do
            match r with 
            | IOitem.Message m -> 
                for c in m.content do
                    match c with 
                    | Output_text t -> yield t.text
                    | _ -> ()
            | _ -> ()
        ]
        |> String.concat " "

    let toImageUri (bytes:byte[]) =
        let imageBytes = System.Convert.ToBase64String bytes
        $"data:image/png;base64,{imageBytes}"

module Api =
    let serOpts = 
        let opts = 
            JsonFSharpOptions.Default()
                .WithUnionInternalTag()
                .WithUnionTagName("type")
                .WithUnionUnwrapRecordCases()
                .WithUnionTagCaseInsensitive()     
                .WithAllowNullFields()
                .WithAllowOverride()
                .ToJsonSerializerOptions()
        opts.WriteIndented <- true
        opts

    //let testRespos = JsonSerializer.Deserialize<Response>(jsonObt, options=serOpts)

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
            if Log.debug_logging then Log.info $"Request: {reqstr} "            
            //use! resp = client.PostAsJsonAsync(builder.Uri, req,options=serOpts)
            use strContent = new StringContent(reqstr,MediaTypeHeaderValue("application/json"))
            use! resp = client.PostAsync(builder.Uri,strContent)
            if resp.StatusCode = Net.HttpStatusCode.OK || resp.StatusCode = Net.HttpStatusCode.Accepted then 
                let! str = resp.Content.ReadAsStringAsync()
                if Log.debug_logging then Log.info $"Response: {str} "
                return JsonSerializer.Deserialize<Response>(str,options=serOpts)            
            else 
                let! str = resp.Content.ReadAsStringAsync()
                if Log.debug_logging then Log.info $"{str} "
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
                   IOitem.Message {Message.Default with content=[Input_text {|text=input|}]}
                ]}) 
            (defaultClient())

