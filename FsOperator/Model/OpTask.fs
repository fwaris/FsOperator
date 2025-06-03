namespace FsOperator
open FsOpCore

type OpTask = {
    id : string
    description : string

    ///The browser will open to this page when the instructions are loaded. 
    //The user may log in or perform other set up before starting the task (in either text or voice mode).
    url : string

    //instructions to be used in text mode 
    textModeInstructions : string 
    voiceAsstInstructions : string 

    //guardrails : string option //to be added later
}

module OpTask =
    let setTextPrompt text opTask = {opTask with textModeInstructions = text}
    let setVoicePrompt text opTask = {opTask with voiceAsstInstructions = text}
    let setUrl url opTask = {opTask with url = url}
    let setId id opTask = {opTask with OpTask.id = id}

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
            url=""
            voiceAsstInstructions=""
            textModeInstructions=""
        }

    module Samples = 

        let sampleAmazon  = 
            {
                id="amazon"
                description="look for a cell phone case"
                url="https://www.amazon.com" 
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
                url="https://www.netflix.com" 
                voiceAsstInstructions=""
                textModeInstructions = """What movies are available on Netflix featuring both Godzilla and King Kong"""
            }

        let sampleTwitter  = 
            {
                id="twitter"
                description="summarize recent gen ai posts"
                url="https://twitter.com" 
                voiceAsstInstructions=""
                textModeInstructions = """Scroll through my Twitter feed and summarize the latest posts
about "Generative AI".
                """
            }

        let sampleLinked  = 
            {
                id="linkedin"
                description="summarize latest posts"
                url="https://linkedin.com" 
                voiceAsstInstructions=""
                textModeInstructions = """Scroll through my LinkedIn feed and summarize the latest posts
about "Generative AI" 
"""
            }

        let allSamples = [sampleAmazon; sampleNetflix; sampleTwitter; sampleLinked]
