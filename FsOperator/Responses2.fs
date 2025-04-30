module Responses2
open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes
open System.Net.Http
open System.Net.Http.Json

type ImageUrl(url:string) =
    member val url : string = url with get, set

[<AbstractClass>]
[<JsonDerivedType(typeof<TextContent>)>]
[<JsonDerivedType(typeof<ImageContent>)>]
type Content(contentType:string) =
    member val ``type`` : string = contentType with get, set
    member val annotations : string list = [] with get, set

and TextContent(text:string) =
    inherit Content("input_text")
    member val text : string = text with get, set

and ImageContent(data:string) =
    inherit Content("input_image")
    member val image_url = ImageUrl(data) with get, set

type Message (role:string, cs:Content list) =
    member val id : string option  = None with get, set
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
    ///Inserts a system (or developer) message as the first
    ///item in the model's context.
    ///When using along with previous_response_id, the instructions
    ///from a previous response will not be carried over to the
    ///next response. This makes it simple to swap out system
    ///(or developer) messages in new responses.
    member val instructions : string = null with get, set
    ///An upper bound for the number of tokens that can be
    ///generated for a response, including visible output
    ///tokens and reasoning tokens.
    member val max_output_tokens : int option = None with get,set
    ///Set of 16 key-value pairs that can be attached to an object.
    ///This can be useful for storing additional information about
    ///the object in a structured format, and querying for objects
    ///via API or the dashboard.
    ///Keys are strings with a maximum length of 64 characters.
    ///Values are strings with a maximum length of 512 characters.
    member val metadata : Map<string,string> option = None with get, set
    ///Whether to allow the model to run tool calls in parallel.
    member val parallel_tool_calls = false with get, set
    ///The unique ID of the previous response to the model. 
    ///Use this to create multi-turn conversations. 
    member val previous_response_id : string = null with get, set
    ///o-series models only. Configuration options for reasoning
    ///models.
    member val reasoning : Reasoning option = None with get, set
    ///Specifies the latency tier to use for processing the
    ///request. This parameter is relevant for customers
    ///subscribed to the scale tier service.
    ///Valid values: auto, default, flex
    member val service_tier = "auto" with get, set
    ///Whether to store the generated model response 
    ///for later retrieval via API.
    member val store = true with get,set
    ///If set to true, the model response data will be streamed 
    ///to the client as it is generated using server-sent
    ///events. See the Streaming section below for more information.
    member val stream = false with get, set
    ///What sampling temperature to use, between 0 and 2. 
    ///Higher values like 0.8 will make the output more 
    ///random, while lower values like 0.2 will make
    ///it more focused and deterministic. 
    ///We generally recommend altering this or top_p but not both.
    member val temperature = 1.0 with get, set
    ///Configuration options for a text response from the model.
    member val text : TextOutput option = None with get, set
    ///How the model should select which tool (or tools) to
    ///use when generating a response.
    ///Valied values: none; auto; required
    member val tool_choice = "auto" with get, set
    ///An list of tools the model may call while generating a response.
    member val tools : Tool list = [] with get, set
    ///An alternative to sampling with temperature, called 
    ///nucleus sampling, where the model considers the 
    ///results of the tokens with top_p probability mass. 
    ///So 0.1 means only the tokens comprising the top 10% 
    ///probability mass are considered.
    ///We generally recommend altering this or temperature but not both.
    member val top_p = 1.0 with get, set
    ///The truncation strategy to use for the model response.
    ///1) auto: If the context of this response and previous ones
    ///exceeds the model's context window size, the model will
    ///truncate the response to fit the context window by dropping
    ///input items in the middle of the conversation.
    ///2) disabled (default): If a model response will exceed the
    ///context window size for a model, the request will fail with a 400 error.
    member val truncation : string = null with get, set //auto, disabled
    ///A unique identifier representing your end-user,
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
    member val status  = "in_progress" with get, set

type FileSearchToolCall() =
    member val id = "" with get, set
    member val queryies 
    member val ``type`` = "file_search_call" with get, set
    member val results : string list = [] with get, set
    member val status  = "in_progress" with get, set

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
    ///An error object returned when the model fails to generate a Response.
    member val error : ResponseError option = None with get, set
    ///Details about why the response is incomplete.
    member val incomplete_details : IncompleteDetails option = None with get, set
    ///Inserts a system (or developer) message as the first item in the model's context.
    ///When using along with previous_response_id, the instructions from a previous response will not be carried 
    ///over to the next response. This makes it simple to swap out system (or developer) messages in new responses.
    member val instructions : string = null with get, set
    ///An upper bound for the number of tokens that can be generated for a response, 
    ///including visible output tokens and reasoning tokens.
    member val max_output_tokens : int option = None with get, set
    ///Set of 16 key-value pairs that can be attached to an object. This can be useful 
    ///for storing additional information about the object in a structured format, 
    ///and querying for objects via API or the dashboard.
    ///Keys are strings with a maximum length of 64 characters. 
    ///Values are strings with a maximum length of 512 characters.
    member val metadata : Map<string,string> option = None with get, set
    ///Model ID used to generate the response, like gpt-4o or o3
    member val model = "" with get, set
    ///An array of content items generated by the model.
    ///The length and order of items in the output array is dependent on the model's response.
    member val output : OutputMessage list = [] with get, set
    ///Whether to allow the model to run tool calls in parallel.
    member val parallel_tool_calls = false with get, set
    ///The unique ID of the previous response to the model.
    ///Use this to create multi-turn conversations. Learn more about
    member val previous_response_id : string = null with get, set
    ///o-series models only. Configuration options for reasoning models.
    member val reasoning : Reasoning option = None with get, set
    ///Specifies the latency tier to use for processing the request. 
    ///This parameter is relevant for customers subscribed to the scale tier service:
    /// auto (default), default, flex
    member val service_tier = "auto" with get, set
    ///The status of the response generation. One of completed, failed, in_progress, or incomplete.
    member val status = "completed" with get, set
    ///What sampling temperature to use, between 0 and 2.
    ///Higher values like 0.8 will make the output more random, 
    ///while lower values like 0.2 will make it more focused and deterministic. 
    //We generally recommend altering this or top_p but not both.
    member val temperature = 1.0 with get, set
    ///Configuration options for a text response from the model. 
    ///Can be plain text or structured JSON data.
    member val text : TextOutput option = None with get, set
    ///How the model should select which tool (or tools) to 
    ///use when generating a response. 
    ///Values: none; auto; required 
    member val tool_choice = "auto" with get, set
    ///An list of tools the model may call while generating a response. 
    ///You can specify which tool to use by setting the tool_choice parameter.
    ///The two categories of tools you can provide the model are:
    ///1) Built-in tools: Tools that are provided by OpenAI that 
    ///extend the model's capabilities, like web search or file search.
    ///2) Function calls (custom tools): Functions that are defined by you,
    ///enabling the model to call your own code.
    member val tools : Tool list = [] with get, set
    ///An alternative to sampling with temperature, called nucleus sampling, 
    ///where the model considers the results of the tokens with top_p probability mass. 
    ///So 0.1 means only the tokens comprising the top 10% probability mass are considered.
    ///We generally recommend altering this or temperature but not both.
    member val top_p = 1.0 with get, set
    ///The truncation strategy to use for the model response.
    ///1) auto: If the context of this response and previous 
    ///ones exceeds the model's context window size, 
    ///the model will truncate the response to fit the context window 
    ///by dropping input items in the middle of the conversation.
    ///2) disabled (default): If a model response will exceed the 
    ///context window size for a model, the request will fail with a 400 error.
    member val truncation : string = null with get, set //auto, disabled
    ///Represents token usage details including input tokens, 
    ///output tokens, a breakdown of output tokens, and the total tokens used.
    member val usage : JsonObject option = None with get, set
    ///A unique identifier representing your end-user, 
    ///which can help OpenAI to monitor and detect abuse
    member val user : string = null with get, set

type Order = Asc | Desc with member this.asString() = match this with Asc -> "asc" | Desc -> "desc"
type ListInputItemsReq(responseId:string) =
    ///The ID of the response to retrieve input items for.
    member val response_id : string = responseId with get, set
    ///An item ID to list items after, used in pagination.
    member val after : string option = None with get, set
    //An item ID to list items before, used in pagination.
    member val before : string option = None with get, set
    ///Additional fields to include in the response. See the include parameter for Response creation above for more information.
    member val ``include`` : string list = [] with get, set
    //A limit on the number of objects to be returned. Limit can range between 1 and 100, and the default is 20.
    member val limit : int  = 20 with get, set
    //The order to return the input items in. Default is asc
    member val order : Order option = None with get, set

type ListItem() = class end

type ListInputItemsResponse() = 
    member val ``object`` = "list" with get, set
    member val data : ListItem list = [] with get, set
    member val first_id : string option = None with get, set
    member val last_id : string option = None with get, set
    member val has_more : bool = false with get, set

type ResponsesReq(responseId:string) = 
    member val response_id = responseId with get, set
    ///Additional fields to include in the response. See the include parameter for Response creation above for more information.
    member val ``include`` : string list = [] with get, set

module service =

    ///Returns a list of input items for a given response.
    let input_items (req:ListInputItemsReq) (client:#HttpClient) =         
        task {            
            let builder = UriBuilder(client.BaseAddress)
            builder.Path <- builder.Path + $"/responses/{req.response_id}/input_items"            
            let query = 
                seq {
                    for r in req.``include`` do
                        yield $"include={r}"
                    match req.after with
                    | Some a -> yield $"after={a}"
                    | None -> ()
                    match req.before with 
                    | Some a -> yield $"before={a}"
                    | None -> ()
                    yield $"limit={req.limit}"
                    match req.order with 
                    | Some order -> yield $"order={order.asString()}"
                    | None -> ()
                }
                |> String.concat "&"
            let! resp = client.GetAsync(builder.Uri)
            return resp.Content.ReadFromJsonAsync<ListInputItemsResponse>()
        }

    let create (req:Request) (client:#HttpClient) =         
        task {            
            let builder = UriBuilder(client.BaseAddress)
            builder.Path <- builder.Path + "/responses"
            let! resp = client.PostAsJsonAsync(builder.Uri, req)
            return resp.Content.ReadFromJsonAsync<Response>()
        }

    ///Retrieves a model response with the given ID.
    let response (req:ResponsesReq) (client:#HttpClient) =         
        task {            
            let builder = UriBuilder(client.BaseAddress)
            builder.Path <- builder.Path + $"/responses/{req.response_id}"
            let query = 
                seq {
                    for r in req.``include`` do
                        yield $"include={r}"
                }
                |> String.concat "&"
            if query.Length > 0 then
                builder.Query <- query
            let! resp = client.GetAsync(builder.Uri)
            return resp.Content.ReadFromJsonAsync<Response>()
        }

    let delete (responseId:string) (client:#HttpClient) =         
        task {            
            let builder = UriBuilder(client.BaseAddress)
            builder.Path <- builder.Path + $"/responses/{responseId}"
            let! resp = client.DeleteAsync(builder.Uri)
            return resp.IsSuccessStatusCode
        }

