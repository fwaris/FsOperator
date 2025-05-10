namespace FsOperator

type InstructionType =
    ///Starting instructions for the CUA model for the text chat mode.
    | TextChat

    ///Instructions for the voice assistant. It is the voice assistant that generates the CUA instructions after conversing with the user.
    | VoiceChat

    ///Assess if the task is done or not. If not, automatically respond to the CUA model prompts to continue the task. (Not implemented yet)
    ///The reaoner model will see chat history + reasoner instructions to generate the response.
    | Reasoner

type ModelPrompts = {``type``:InstructionType; prompt:string} 

type Instructions = {
    id : string
    description : string

    ///The browser will open to this page when the instructions are loaded. 
    //The user may log in or perform other set up before starting the task (in either text or voice mode).
    startUrl : string

    ///List of instructions - one per instruction type (if there are multiple of the same type the first one is used)
    prompts : ModelPrompts list

    //guardrails : string option //to be added later
}


module Instructions =
    let sample  = 
        {
            id="amazon"
            description="look for a cell phone case"
            startUrl="https://www.amazon.com" 
            prompts = [
                {
                    ``type``= TextChat; 
                    prompt="""On Amazon, find me an iphone 16 pro max case that has 
**built in screen protector** and is less than $50 with good rating.
*make sure the price is less than $50*
Use the search box to find products. 
**Ignore any sign-in pages and continue without signing in**
I just want to search for products not purchase them yet."""
                }

                {
                    ``type``= Reasoner
                    prompt="not yet implemented"}
                
                
                {
                    ``type``= VoiceChat
                    prompt="""
You are to collaborate with a user to help complete a task.
The task is performed by a separate 'assistant'. 
You job is to converse with the human user to generate and sumbit the instructions to the assistant.
The assistant will carry out the task and return with a response - which may be a question or a clarification.
Again, converse with the user before generating the next instruction for the assistant.
Always confirm with the user first before sending the instructions to the assistant.
"""}
            ]
        }

    let setPrompt instrType prompt instructions =
        let newPrompts = 
            instructions.prompts
            |> List.map (fun p -> if p.``type`` = instrType then {p with prompt=prompt} else p)
        {instructions with prompts=newPrompts}

    let setTextChat text instructions = instructions |> setPrompt TextChat text
    let setVoiceChat text instructions = instructions |> setPrompt VoiceChat text
    let setReasoner text instructions = instructions |> setPrompt Reasoner text

    let getTextChat instructions = 
        instructions.prompts
        |> List.tryFind (fun p -> p.``type`` = TextChat)
        |> Option.map (fun p -> p.prompt)
        |> Option.defaultValue ""

    let getVoiceChat instructions =
        instructions.prompts
        |> List.tryFind (fun p -> p.``type`` = VoiceChat)
        |> Option.map (fun p -> p.prompt)
        |> Option.defaultValue ""

    let getReasoner instructions =
        instructions.prompts
        |> List.tryFind (fun p -> p.``type`` = Reasoner)
        |> Option.map (fun p -> p.prompt)
        |> Option.defaultValue ""
