namespace FsOpCore

type AsstMsg = {
    id      : string    
    content : string
}

type ChatMsg = User of string | Assistant of AsstMsg

type Chat = { systemMessage:string option; question:string option; messages:ChatMsg list ; prompt:bool}
    with static member Default = { systemMessage = None; question = None; messages = [] ; prompt=false}

module Chat = 
    let private updateWith f cs = cs |> List.map f
    let private appendMsg (msg:ChatMsg) (history:ChatMsg list) = history @ [msg] 
    let append msg chat = {chat with messages = appendMsg msg chat.messages}
    let setSystemMessage instructions chat = {chat with systemMessage = Some instructions}
    let setQuestion question chat = {chat with question = Some question}
    let getSystemMessage chat = chat.systemMessage |> Option.defaultValue ""
    let getQuestion chat = chat.question |> Option.defaultValue ""
    let setPrompt b chat = {chat with prompt = b}


