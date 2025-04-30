﻿module ResponsesApi
open System
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


let options = 
   JsonFSharpOptions.Default()
    .WithUnionInternalTag()
    .WithUnionTagName("type")


// type Response2 = {
//     id : string
//     ``object`` : string
//     created_at : int64
//     status : string
//     error : ResponseError option
//     incomplete_details : IncompleteDetails option
//     instructions : string option
//     max_output_tokens : int option
//     model : string
//     output : OutputMessage list
//     parallel_tool_calls : bool
//     previous_response_id : string option
//     reasoning : Reasoning option
//     store : bool
//     temperature : float32
//     text : TextOutput option
//     tool_choice : string
//     tools : Tool list
//     top_p : float32
//     truncation : string option //auto, disabled
//     usage : JsonObject option
//     user : string option
// }
