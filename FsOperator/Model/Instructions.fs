namespace FsOperator

type InstructionType =
    ///Starting instructions for the CUA model for the text chat mode.
    | TextChat

    ///Instructions for the voice assistant. It is the voice assistant that generates the CUA instructions after conversing with the user.
    | VoiceChat


type Instructions = {
    id : string
    description : string

    ///The browser will open to this page when the instructions are loaded. 
    //The user may log in or perform other set up before starting the task (in either text or voice mode).
    startUrl : string

    ///List of instructions - one per instruction type (if there are multiple of the same type the first one is used)
    textPrompt : string 
    voicePrompt : string 

    //guardrails : string option //to be added later
}

module Instructions =
    let setTextPrompt text instructions = {instructions with textPrompt = text}
    let setVoicePrompt text instructions = {instructions with voicePrompt = text}

    let private defaultVoicePrompt = """
You are to collaborate with a user to help complete a task.
The task is performed by a separate 'assistant'. 
You job is to converse with the human user to generate and sumbit the instructions to the assistant.
The assistant will carry out the task and return with a response - which may be a question or a clarification.
Again, converse with the user before generating the next instruction for the assistant.
Always confirm with the user first before sending the instructions to the assistant."""

    let sample  = 
        {
            id="amazon"
            description="look for a cell phone case"
            startUrl="https://www.amazon.com" 
            voicePrompt= defaultVoicePrompt
            textPrompt = """On Amazon, find me an iphone 16 pro max case that has 
**built in screen protector** and is less than $50 with good rating.
*make sure the price is less than $50*
Use the search box to find products. 
**Ignore any sign-in pages and continue without signing in**
I just want to search for products not purchase them yet."""

        }


    let sampleNetflix  = 
        {
            id="netflix"
            description="scifi movies"
            startUrl="https://www.netflix.com" 
            voicePrompt=defaultVoicePrompt
            textPrompt = """On netflix.com search for well rate scifi movies 
and give me a list."""
        }

    let sampleTwitter  = 
        {
            id="twitter"
            description="scifi movies"
            startUrl="https://twitter.com" 
            voicePrompt=defaultVoicePrompt
            textPrompt = """on twitter find out if anyone has posted about 
generative AI in the recent past and 
summarize the postings"""
        }

    let sampleLinked  = 
        {
            id="linkedin"
            description="summarize latest posts"
            startUrl="https://linkedin.com" 
            voicePrompt=defaultVoicePrompt
            textPrompt = """Summarize what my connections have posted today
on LinkedIn."""
        }
