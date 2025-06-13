namespace FsOperator
open System
open FsOpCore
open System
open System.Text.Json
open System.Text.Json.Serialization

type TaskTarget = TProcess of string*string option | TLink of string

type OpTask = {
    id : string
    description : string

    ///The browser will open to this page when the instructions are loaded. 
    //The user may log in or perform other set up before starting the task (in either text or voice mode).
    target : TaskTarget

    //instructions to be used in text mode 
    textModeInstructions : string 
    voiceAsstInstructions : string 

    //guardrails : string option //to be added later
}

module OpTask =
    let setTextPrompt text opTask = {opTask with textModeInstructions = text}
    let setVoicePrompt text opTask = {opTask with voiceAsstInstructions = text}
    let setUrl url (opTask:OpTask) = {opTask with target = TLink url}
    let setProcess name arg (opTask:OpTask) = {opTask with target = TProcess (name,arg)}
    let setTarget tgt  (opTask:OpTask) = {opTask with target = tgt}
    let setId id opTask = {opTask with OpTask.id = id}
    let targetToString = function TLink url -> url | TProcess (a,b) -> match b with Some b -> $"{a} {b}" | None -> a
    let isEmptyTarget = function TLink url -> String.IsNullOrWhiteSpace url | _ -> false

    let private _parseTarget (xs:string array) =
        let a = xs.[0]
        let b = if xs.Length > 1 then Some xs.[1] else None
        if a.EndsWith ".exe" then 
            TProcess (a,b)
        else 
            let a = if a.StartsWith("http") then a else "https://" + a
            TLink a

    let parseTarget (tgt:string) = 
        let tgt = tgt.Trim()
        let xs = tgt.Split(" ", StringSplitOptions.RemoveEmptyEntries)
        _parseTarget xs

    let serOpts() = 
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()

    let serialize (str:IO.Stream) (task:OpTask) =         
        JsonSerializer.Serialize(str,task,options=serOpts())

    let deserialize (str:IO.Stream) = 
        JsonSerializer.Deserialize<OpTask>(str,options=serOpts())

                
    let defaultVoicePrompt = """You are to collaborate with a user to help complete a task.
The task is actually performed by a separate 'assistant'. 
The assistant has the capability to perform computer actions if instructed.
**YOU CAN ASK THE assitant TO GOTO WEB PAGES AND PERFORM ACTIONS ON THEM.**
**Use the function 'gotoUrl' to ask the assistant to go to a specific URL.**
For example, the assistant can open web pages and browse through them to get infomation.
Use the supplied tools and functions to instruct the assistant to perform actions.
Your job is to converse with the human user to generate and sumbit the instructions to the assistant.
The assistant will carry out the task and return with a response - which may be a question or a clarification.
Again, converse with the user before generating the next instruction for the assistant.
Always confirm with the user first before sending the instructions to the assistant.
"""

    let voicePromptOrDefault (instr:string) = if isEmpty instr then defaultVoicePrompt else instr

    let empty = 
        {
            id="blank"
            description=""
            target = TLink ""
            voiceAsstInstructions=""
            textModeInstructions=""
        }

    module Samples = 

        let sampleAmazon  = 
            {
                id="amazon"
                description="look for a cell phone case"
                target= TLink "https://www.amazon.com" 
                voiceAsstInstructions= ""
                textModeInstructions = """On Amazon, find me an iphone 16 pro max case that has 
    **built in screen protector**. 
    Find me the top rated case regardless of price.
    **Ignore any sign-in pages and continue without signing in**
    I just want to search for products not purchase them yet."""
            }


        let sampleNetflix  = 
            {
                id="netflix"
                description="Godzilla and Kong movies"
                target = TLink "https://www.netflix.com" 
                voiceAsstInstructions=""
                textModeInstructions = """What movies are available on Netflix featuring both Godzilla and King Kong"""
            }

        let sampleTwitter  = 
            {
                id="twitter"
                description="summarize recent gen ai posts"
                target = TLink "https://twitter.com" 
                voiceAsstInstructions=""
                textModeInstructions = """Scroll through my Twitter feed and summarize the latest posts
about "Generative AI".
                """
            }

        let sampleLinked  = 
            {
                id="linkedin"
                description="summarize latest posts"
                target = TLink "https://linkedin.com" 
                voiceAsstInstructions=""
                textModeInstructions = """Scroll through my LinkedIn feed and summarize the latest posts
about "Generative AI" 
"""
            }

        let allSamples = [sampleAmazon; sampleNetflix; sampleTwitter; sampleLinked]
