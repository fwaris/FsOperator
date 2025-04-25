module ResponsesApi
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes

type ImageUrl(url:string) =
    member val url : string = url with get, set

[<AbstractClass>]
[<JsonDerivedType(typeof<TextContent>)>]
[<JsonDerivedType(typeof<ImageContent>)>]
type Content(contentType:string) =
    member val ``type`` : string = contentType with get, set

and TextContent(text:string) =
    inherit Content("input_text")
    member val text : string = text with get, set

and ImageContent(data:string) =
    inherit Content("input_image")
    member val image_url = ImageUrl(data) with get, set

type Message (role:string, cs:Content list) =
    member val role : string = role with get, set
    member val content : Content list  = cs with get, set

[<AbstractClass>]
[<JsonDerivedType(typeof<FileSearchTool>)>]
[<JsonDerivedType(typeof<WebSearchTool>)>]
[<JsonDerivedType(typeof<FunctionTool>)>]
type Tool(toolType:string) =
    member val ``type`` : string = toolType with get, set
and FileSearchTool() =
    inherit Tool("file_search")
    member val vector_store_ids : string list = [] with get, set
    member val max_num_results : int = 20 with get, set
and WebSearchTool() =
    inherit Tool("web_search_preview")
    member val vector_store_ids : string list = [] with get, set
    member val max_num_results : int = 20 with get, set
and FunctionTool() =
    inherit Tool("function")
    member val name = "" with get, set
    member val description = "" with get, set
    member val parameters = Parameter() with get, set
and Parameter() =
    member val ``type`` : string = "object" with get, set
    member val properties : Map<string,Property> = Map.empty with get, set
    member val required = [] with get, set
and Property() =
    member val ``type`` = "string" with get, set
    member val description = "" with get, set
    member val enum : string list = [] with get, set

type Reasoning() =
    member val effort = "high" with get, set
    member val summary : string option = None with get, set

module Include =
    let ``file_search_call.results`` = "file_search_call.results"
    let ``message.input_image.image_url`` = "message.input_image.image_url"
    let ``computer_call_output.output.image_url`` = "computer_call_output.output.image_url"

type TextOutputFormat(outputType:string) =
    member val ``type`` = outputType with get, set
    static member json_schema_type = TextOutputFormat("json_schema")
    static member text_type = TextOutputFormat("text")

[<AbstractClass>]
[<JsonDerivedType(typeof<TextOutputText>)>]
[<JsonDerivedType(typeof<TextOutputJsonSchema>)>]
type TextOutput(format) =
    member val format : TextOutputFormat = format with get, set
and TextOutputText() =
    inherit TextOutput(TextOutputFormat.text_type)
    member val text : string = "text" with get, set
and TextOutputJsonSchema(name:string,schema:JsonElement) =
    inherit TextOutput(TextOutputFormat.json_schema_type)
    member val name = name with get, set
    member val ``type`` : string = "json_schema" with get, set
    member val description = "" with get, set
    member val strict = false with get, set
    member val schema = schema with get, set

type Request(msgs:Message list) =
    member val model = "gpt-4.1" with get, set
    member val ``include`` : string list = [] with get, set
    member val input = msgs
    member val instructions : string = null with get, set
    member val max_output_tokens : int option = None with get,set
    member val metadata : Map<string,string> option = None with get, set
    member val parallel_tool_calls = false with get, set
    member val previous_response_id : string = null with get, set
    member val reasoning : Reasoning option = None with get, set
    member val service_tier = "auto" with get, set
    member val store = true with get,set
    member val stream = false with get, set
    member val temperature = 1.0 with get, set
    member val text : TextOutput option = None with get, set
    member val tool_choice = "auto" with get, set
    member val tools : Tool list = [] with get, set
    member val top_p = 1.0 with get, set
    member val truncation : string = null with get, set //auto, disabled
    member val user = null with get, set

type OutputContent(contentType:string) =
    member val ``type`` = contentType with get, set

type TextOutputContent() =
    inherit OutputContent("output_text")
    member val text = "" with get, set
    member val annotations : string list = [] with get, set

type OutputMessage() =
    member val id = "" with get, set
    member val ``type`` = "message" with get, set
    member val role = "assistant" with get, set
    member val content : OutputContent list = [] with get, set

type ResponseError() =
    member val code = "" with get, set
    member val message = "" with get, set

type IncompleteDetails() =
    member val reason = "" with get, set

type InputTokenDetails() =
    member val cached_tokens = 0 with get, set

type OutputTokenDetails() =
    member val reasoning_tokens = 0 with get, set

type Usage() =
    member val input_tokens = 0 with get, set
    member val input_token_details : InputTokenDetails = InputTokenDetails() with get, set
    member val output_tokens = 0 with get, set
    member val output_token_details : OutputTokenDetails = OutputTokenDetails() with get, set
    member val total_tokens = 0 with get, set

type Response() =
    member val id = "" with get,set
    member val ``object`` = "response" with get, set
    member val created_at = 0L with get, set //unix timestamp in seconds
    member val error : ResponseError option = None with get, set
    member val incomplete_details : IncompleteDetails option = None with get, set
    member val instructions : string = null with get, set
    member val max_output_tokens : int option = None with get, set
    member val metadata : Map<string,string> option = None with get, set
    member val model = "" with get, set
    member val output : OutputMessage list = [] with get, set
    member val parallel_tool_calls = false with get, set
    member val previous_response_id : string = null with get, set
    member val reasoning : Reasoning option = None with get, set
    member val service_tier = "auto" with get, set
    member val status = "completed" with get, set
    member val temperature = 1.0 with get, set
    member val text : TextOutput option = None with get, set
    member val tool_choice = "auto" with get, set
    member val tools : Tool list = [] with get, set
    member val top_p = 1.0 with get, set
    member val truncation : string = null with get, set //auto, disabled
    member val usage : JsonObject option = None with get, set
    member val user : string = null with get, set

