namespace FsOpCore

module C =
    let MAX_BUS_QUEUE_DEPTH = 29
    
    let DEBUG_PORT = 9222
    let REMOTE_BROWSER_PORT = 51400

    let OPENAI_RT_API = "https://api.openai.com/v1/realtime"
    let OPENAI_SESSION_API = "https://api.openai.com/v1/realtime/sessions"
    let OPENAI_RT_MODEL_GPT4O = "gpt-4o-realtime-preview"
    let OPENAI_RT_MODEL_GPT4O_MINI = "gpt-4o-mini-realtime-preview"
    let REASONER_MODEL = FsResponses.Models.gpt_5_mini // // FsResponses.Models.o4_mini // FsResponses.Models.gpt_41

    let WIN_TITLE = "FsOperator"

    let MAX_MESSAGE_HISTORY = 10

    let VIEWPORT_WIDTH = 1280
    let VIEWPORT_HEIGHT = 768

    let CORR_ID = "correlationId"

    let PLAYWRIGHT_DEFAULT_TIMEOUT = 60000f

    let MAX_CUA_CALLS_IN_TASK = 20
    let MAX_ACTIONS = 3
    let FUNCTION_CALL = "function_call"
    
    let INITIAL="initial"
    let YES="yes"

   