namespace FsOperator

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
    let setId id opTask = {opTask with id = id}

    let defaultVoicePrompt = """You are to collaborate with a user to help complete a task.
The task is actually performed by a separate 'assistant'. 
The assistant has the capability to perform computer actions if instructed.
You job is to converse with the human user to generate and sumbit the instructions to the assistant.
The assistant will carry out the task and return with a response - which may be a question or a clarification.
Again, converse with the user before generating the next instruction for the assistant.
Always confirm with the user first before sending the instructions to the assistant.
"""

    let empty = 
        {
            id="blank"
            description=""
            url=""
            voiceAsstInstructions=defaultVoicePrompt
            textModeInstructions = ""
        }

    let sample  = 
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
            description="scifi movies"
            url="https://www.netflix.com" 
            voiceAsstInstructions=""
            textModeInstructions = """On netflix.com search for well rated scifi movies 
and give me a list."""
        }

    let sampleTwitter  = 
        {
            id="twitter"
            description="summarize recent gen ai posts"
            url="https://twitter.com" 
            voiceAsstInstructions=""
            textModeInstructions = """on twitter find out if anyone has posted about 
generative AI in the recent past and 
summarize the postings"""
        }

    let sampleLinked  = 
        {
            id="linkedin"
            description="summarize latest posts"
            url="https://linkedin.com" 
            voiceAsstInstructions=""
            textModeInstructions = """Summarize what my connections have posted today
on LinkedIn."""
        }
