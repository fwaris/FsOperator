namespace FsOpCore
open FsResponses

type AsstMsg = {
    id      : string    
    content : string
}

type ChatMsg = 
    | User of string 
    | Reasoner of string
    | Assistant of AsstMsg 


