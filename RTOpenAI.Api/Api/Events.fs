﻿namespace rec RTOpenAI.Api.Events
open System
open System.Text.Json
open System.Text.Json.Serialization
open RTOpenAI
//generated by o1 from openai realtime api docs scraped from website
//default static properties added by github copilot
//default values added manually

(* Codegen notes
- o1 largely got all objects correct (almost 450 lines of code with no compile errors)
- However several corrections were needed to make event definitions correct as per API documentation
- Strongly typed properties and json converters added manually
- o1 missed response.audio_... and later events (maybe due to token limit set in the codegen api call)
*)

// Shared Types
type InputAudioTranscription =
    {
        model: string
    }
    static member Default = { model = "whisper-1"}

type TurnDetection =
    {
        ``type``: string option
        threshold: float option
        prefix_padding_ms: int option
        silence_duration_ms: int option
    }
    static member Default = { 
        ``type`` = Some "server_vad" // None to turn off
        threshold = Some 0.5
        prefix_padding_ms = Some 300  // How much audio to include in the audio stream before the speech starts.
        silence_duration_ms = Some 200 //// How long to wait to mark the speech as stopped.
    }

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
    static member Default = { ``type`` = ""; properties = Map.empty; required = [] }

type Tool =
    {
        ``type``: string
        name: string
        description: string
        parameters: Parameters
    }
    static member Default = { ``type`` = ""; name = ""; description = ""; parameters = Parameters.Default }

 type Client_Secret = {
     expires_at : int64
     value : string
 }

type Session =
    {
        ///unique id of the session
        id: string option
        ///The object type, must be "realtime.session".
        ``object`` : string option
        ///The default model used for this session.
        model: string option
        modalities: string list
        instructions: string option
        voice: string option
        input_audio_format: string option
        output_audio_format: string option
        input_audio_transcription: InputAudioTranscription option
        turn_detection: TurnDetection option
        tools: Tool list
        tool_choice: string option
        temperature: float option
        max_output_tokens: int option
        client_secret : Client_Secret option
    }
    static member Default = 
        { 
            id = None
            modalities = [] 
            ``object`` = None
            model = None
            instructions = None
            voice = None
            input_audio_format = None
            output_audio_format = None 
            input_audio_transcription = None 
            turn_detection = None
            tools = []
            tool_choice = None //How the model chooses tools. Options are "auto", "none", "required", or specify a function.
            temperature = None
            max_output_tokens = None 
            client_secret = None
        }

type ErrorDetail =
    {
        ``type``: string
        code: string
        message: string
        param: string option
        event_id: string option
    }
    static member Default = { ``type`` = ""; code = ""; message = ""; param = None; event_id = None }

type Usage =
    {
        total_tokens: int
        input_tokens: int
        output_tokens: int
    }
    static member Default = { total_tokens = 0; input_tokens = 0; output_tokens = 0 }

/// Send this event to update the session’s default configuration.
type SessionUpdateEvent =
    {
        event_id: string
        ``type``: string  // "session.update"
        session: Session
    }
    static member Default = { event_id = ""; ``type`` = "session.update"; session = Session.Default }

///Send this event to append audio bytes to the input audio buffer.
type InputAudioBufferAppendEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.append"
        audio: string  // Base64 encoded audio data
    }
    static member Default = { event_id = ""; ``type`` = "input_audio_buffer.append"; audio = "" }

///Send this event to commit audio bytes to a user message.
type InputAudioBufferCommitEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.commit"
    }
    static member Default = { event_id = ""; ``type`` = "input_audio_buffer.commit" }

///Send this event to clear the audio bytes in the buffer.
type InputAudioBufferClearEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.clear"
    }
    static member Default = { event_id = ""; ``type`` = "input_audio_buffer.clear" }

///Send this event when adding an item to the conversation.
type ConversationItemCreateEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.create"
        previous_item_id: string option
        item: ConversationItem
    }
    static member Default = { event_id = ""; ``type`` = "conversation.item.create"; previous_item_id = None; item = ConversationItem.Default }

///Send this event when you want to truncate a previous assistant message’s audio.
type ConversationItemTruncateEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.truncate"
        item_id: string
        content_index: int
        audio_end_ms: int
    }
    static member Default = { event_id = ""; ``type`` = "conversation.item.truncate"; item_id = ""; content_index = 0; audio_end_ms = 0 }

///Send this event when you want to remove any item from the conversation history.
type ConversationItemDeleteEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.delete"
        item_id: string
    }
    static member Default = { event_id = ""; ``type`` = "conversation.item.delete"; item_id = "" }

///Send this event to trigger a response generation.
type ResponseCreateEvent =
    {
        event_id: string
        ``type``: string  // "response.create"
        response: Response
    }
    static member Default = { event_id = ""; ``type`` = "response.create"; response = Response.Default }

///Send this event to cancel an in-progress response.
type ResponseCancelEvent =
    {
        event_id: string
        ``type``: string  // "response.cancel"
    }
    static member Default = { event_id = ""; ``type`` = "response.cancel" }

type ContentType =
    | Input_text 
    | Input_audio
    | Text 
    | Audio 

type ContentTypeConverter() =
    inherit JsonConverter<ContentType>()

    override _.Read(reader, _, _) =
        let value = reader.GetString()
        match value with
        | "input_text" -> ContentType.Input_text
        | "input_audio" -> ContentType.Input_audio
        | "text" -> ContentType.Text
        | "audio" -> ContentType.Audio
        | _ -> failwith $"Invalid ContentType value: {value}"
    
    override _.Write(writer, value, _) =
        let value = 
            match value with
            | ContentType.Input_text -> "input_text"
            | ContentType.Input_audio -> "input_audio"
            | ContentType.Text -> "text"
            | ContentType.Audio -> "audio"
        writer.WriteStringValue(value)

type ConversationItemContent =
    {
        [<JsonConverter(typeof<ContentTypeConverter>)>]
        ``type``: ContentType
        id : string option
        text: string option
        audio: string option
        transcript: string option
    }
    static member Default = { ``type`` = ContentType.Input_audio; id=None; text = None; transcript = None; audio=None}

type ConversationItemType =
    | Message 
    | Function_call
    | Function_call_output

type ConversationItemTypeConverter() =
    inherit JsonConverter<ConversationItemType>()

    override _.Read(reader, _, _) =
        let value = reader.GetString()
        match value with
        | "message" -> ConversationItemType.Message
        | "function_call" -> ConversationItemType.Function_call
        | "function_call_output" -> ConversationItemType.Function_call_output
        | _ -> failwith $"Invalid ConversationItemType value: {value}"
    
    override _.Write(writer, value, _) =
        let value = 
            match value with
            | ConversationItemType.Message -> "message"
            | ConversationItemType.Function_call -> "function_call"
            | ConversationItemType.Function_call_output -> "function_call_output"
        writer.WriteStringValue(value)

type ConversationItem =
    {
        id: string option
        ``object``: string option //The object type, must be "realtime.item".
        [<JsonConverter(typeof<ConversationItemTypeConverter>)>]
        ``type``: ConversationItemType
        status: string option
        role: string option
        content: ConversationItemContent list option
        call_id : string option
        name : string option
        arguments : string option
        output : string option
        audio : int16[] list option
   }
   with static member Default = 
                { 
                    id = None
                    ``object`` = None
                    ``type`` = ConversationItemType.Message
                    status = None
                    role = None
                    content = None
                    call_id = None
                    name = None
                    arguments = None
                    output = None
                    audio = None
                }

type Response =
    {
        modalities: string list option
        instructions: string option
        voice: string option
        output_audio_format: string option
        tools: Tool list option
        tool_choice: string option
        temperature: float option
        max_output_tokens: int option
    }
    static member Default = 
        { 
            modalities = None; instructions = None; voice = None; output_audio_format = None; tools = None
            tool_choice = None; temperature = None; max_output_tokens = None 
        }

// Server Event Record Types

///These are events emitted from the OpenAI Realtime WebSocket server to the client.
type ErrorEvent =
    {
        event_id: string
        ``type``: string  // "error"
        error: ErrorDetail
    }

///Returned when a session is created. Emitted automatically when a new connection is established.
type SessionCreatedEvent =
    {
        event_id: string
        ``type``: string  // "session.created"
        session: Session
    }

///Returned when a session is updated.
type SessionUpdatedEvent =
    {
        event_id: string
        ``type``: string  // "session.updated"
        session: Session
    }

///Returned when a conversation item is created.
type ConversationCreatedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.created"
        conversation: Conversation
    }

///Returned when a conversation item is created.
type ConversationItemCreatedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.created"
        previous_item_id: string option
        item: ConversationItem
    }

///Returned when input audio transcription is enabled and a transcription succeeds.
type ConversationItemInputAudioTranscriptionCompletedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.input_audio_transcription.completed"
        item_id: string
        content_index: int
        transcript: string
    }

///Returned when input audio transcription is configured, and a transcription request for a user message failed.
type ConversationItemInputAudioTranscriptionFailedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.input_audio_transcription.failed"
        item_id: string
        content_index: int
        error: ErrorDetail
    }

///Returned when an earlier assistant audio message item is truncated by the client.
type ConversationItemTruncatedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.truncated"
        item_id: string
        content_index: int
        audio_end_ms: int
    }

///Returned when an item in the conversation is deleted.
type ConversationItemDeletedEvent =
    {
        event_id: string
        ``type``: string  // "conversation.item.deleted"
        item_id: string
    }

///Returned when an input audio buffer is committed, either by the client or automatically in server VAD mode.
type InputAudioBufferCommittedEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.committed"
        previous_item_id: string option
        item_id: string
    }

///Returned when the input audio buffer is cleared by the client.
type InputAudioBufferClearedEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.cleared"
    }

///Returned in server turn detection mode when speech is detected.
type InputAudioBufferSpeechStartedEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.speech_started"
        audio_start_ms: int
        item_id: string
    }

///Returned in server turn detection mode when speech stops.
type InputAudioBufferSpeechStoppedEvent =
    {
        event_id: string
        ``type``: string  // "input_audio_buffer.speech_stopped"
        audio_end_ms: int
        item_id: string
    }

///Returned when a new Response is created. The first event of response creation, where the response is in an initial state of "in_progress".
type ResponseCreatedEvent =
    {
        event_id: string
        ``type``: string  // "response.created"
        response: ResponseDetails
    }

///Returned when a Response is done streaming. Always emitted, no matter the final state.
type ResponseDoneEvent =
    {
        event_id: string
        ``type``: string  // "response.done"
        response: ResponseDetails
    }

///Returned when a new Item is created during response generation.
type ResponseOutputItemAddedEvent =
    {
        event_id: string
        ``type``: string  // "response.output_item.added"
        response_id: string
        output_index: int
        item: ResponseOutputItemItem
    }

///Returned when an Item is done streaming. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseOutputItemDoneEvent =
    {
        event_id: string
        ``type``: string  // "response.output_item.done"
        response_id: string
        output_index: int
        item: ResponseOutputItemItem
    }

///Returned when a new content part is added to an assistant message item during response generation.
type ResponseContentPartAddedEvent =
    {
        event_id: string
        ``type``: string  // "response.content_part.added"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        part: ContentPart
    }

///Returned when a content part is done streaming in an assistant message item. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseContentPartDoneEvent =
    {
        event_id: string
        ``type``: string  // "response.content_part.done"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        part: ContentPart
    }

///Returned when the text value of a "text" content part is updated.
type ResponseTextDeltaEvent =
    {
        event_id: string
        ``type``: string  // "response.text.delta"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        delta: string
    }

///Returned when the text value of a "text" content part is done streaming. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseTextDoneEvent =
    {
        event_id: string
        ``type``: string  // "response.text.done"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        text: string
    }

///Returned when the model-generated transcription of audio output is updated.
type ResponseAudioTranscriptDeltaEvent =
    {
        event_id: string
        ``type``: string  // "response.text.done"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        delta: string
    }

///Returned when the model-generated transcription of audio output is done streaming. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseAudioTranscriptDoneEvent =
    {
        event_id: string
        ``type``: string  // "response.text.done"
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        transcript: string
    }


///Returned when the model-generated function call arguments are done streaming. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseFunctionCallArgumentsDoneEvent = 
    {
        event_id: string
        ``type``: string
        response_id: string
        item_id: string
        output_index: int
        call_id: string
        arguments: string
    }

///Returned when the model-generated audio is done. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseFunctionCallArgumentsDeltaEvent = 
    {
        event_id: string
        ``type``: string
        response_id: string
        item_id: string
        output_index: int
        call_id: string
        delta: string
    }

///Returned when the model-generated audio is updated.
type ResponseAudioDeltaEvent = 
    {
        event_id: string
        ``type``: string
        response_id: string
        item_id: string
        output_index: int
        content_index: int
        delta: string
    }

///Returned when the model-generated audio is done. Also emitted when a Response is interrupted, incomplete, or cancelled.
type ResponseAudioDoneEvent = 
    {
        event_id: string
        ``type``: string
        response_id: string
        item_id: string
        output_index: int
        content_index: int
    }

type RateLimit =
    {
        name : string
        limit : int
        remaining : int
        reset_seconds : float
    }
 
///Emitted after every "response.done" event to indicate the updated rate limits.
type RateLimitsUpdatedEvent =
    {
        event_id: string
        ``type``: string  // "rate_limits.updated"
        rate_limits: RateLimit list
    }

type Conversation =
    {
        id: string
        ``object``: string
    }
    
type StatusError =
    {
        ``type`` : string //usually "server_error"
        code : JsonDocument option
        message : string option
    }
    
type StatusDetails =
        {
            ``type`` : string
            error : StatusError
        }

type ResponseDetails =
    {
        id: string
        ``object``: string
        status: string
        status_details: StatusDetails option
        output: ResponseOutputItem list
        usage: Usage option
    }

type ResponseOutputItem =
    {
        id: string
        ``object``: string
        ``type``: string
        status: string
        role: string
        content: ConversationItemContent list
    }
    
type ResponseOutputItemItem =
    {
        id: string
        ``object``: string
        ``type``: string //function_call, function_call_output
        status: string
        role: string
        content: ConversationItemContent list
        call_id : string
        name : string option
        arguments : string option
        output : string option
    }

type ContentPart =
    {
        ``type``: string
        text: string option
        delta: string option
        transcript: string option
    }
    

type ServerEvent =
    | Error of ErrorEvent
    | SessionCreated of SessionCreatedEvent
    | SessionUpdated of SessionUpdatedEvent
    | ConversationCreated of ConversationCreatedEvent
    | ConversationItemCreated of ConversationItemCreatedEvent
    | ConversationItemInputAudioTranscriptionCompleted of ConversationItemInputAudioTranscriptionCompletedEvent
    | ConversationItemInputAudioTranscriptionFailed of ConversationItemInputAudioTranscriptionFailedEvent
    | ConversationItemTruncated of ConversationItemTruncatedEvent
    | ConversationItemDeleted of ConversationItemDeletedEvent
    | InputAudioBufferCommitted of InputAudioBufferCommittedEvent
    | InputAudioBufferCleared of InputAudioBufferClearedEvent
    | InputAudioBufferSpeechStarted of InputAudioBufferSpeechStartedEvent
    | InputAudioBufferSpeechStopped of InputAudioBufferSpeechStoppedEvent
    | ResponseCreated of ResponseCreatedEvent
    | ResponseDone of ResponseDoneEvent
    | ResponseOutputItemAdded of ResponseOutputItemAddedEvent
    | ResponseOutputItemDone of ResponseOutputItemDoneEvent
    | ResponseContentPartAdded of ResponseContentPartAddedEvent
    | ResponseContentPartDone of ResponseContentPartDoneEvent
    | ResponseTextDelta of ResponseTextDeltaEvent
    | ResponseTextDone of ResponseTextDoneEvent
    | ResponseAudioTranscriptDelta of ResponseAudioTranscriptDeltaEvent
    | ResponseAudioTranscriptDone of ResponseAudioTranscriptDoneEvent
    | ResponseAudioDelta of ResponseAudioDeltaEvent
    | ResponseAudioDone of ResponseAudioDoneEvent
    | ResponseFunctionCallArgumentsDelta of ResponseFunctionCallArgumentsDeltaEvent
    | ResponseFunctionCallArgumentsDone of ResponseFunctionCallArgumentsDoneEvent
    | RateLimitsUpdated of RateLimitsUpdatedEvent
    | UnknownEvent of string * JsonDocument


type ClientEvent =
    | SessionUpdate of SessionUpdateEvent
    | InputAudioBufferAppend of InputAudioBufferAppendEvent
    | InputAudioBufferCommit of InputAudioBufferCommitEvent
    | InputAudioBufferClear of InputAudioBufferClearEvent
    | ConversationItemCreate of ConversationItemCreateEvent
    | ConversationItemTruncate of ConversationItemTruncateEvent
    | ConversationItemDelete of ConversationItemDeleteEvent
    | ResponseCreate of ResponseCreateEvent
    | ResponseCancel of ResponseCancelEvent

