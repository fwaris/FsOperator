namespace FsOperator

type AsstMsg = {
    id      : string    
    content : string
}

type ChatMsg = User of string | Assistant of AsstMsg

module Chat = 
    let updateWith f cs = cs |> List.map f
    let append (msg:ChatMsg) (history:ChatMsg list) = history @ [msg] 

