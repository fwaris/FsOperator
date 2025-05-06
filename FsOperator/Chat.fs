namespace FsOperator

type AsstMsg = {
    id      : string    
    content : string
}

type ChatMsg = User of string | Assistant of AsstMsg | Question of string | Placeholder

module Chat = 
    let updateWith f cs = cs |> List.map f
    let fixPlaceholder (history:ChatMsg list) = 
        match history with 
        | [] -> [Placeholder]
        | xs -> let xs = xs |> List.filter (fun x -> not x.IsPlaceholder) 
                if (List.last xs).IsUser then xs else xs @ [Placeholder]
    let append (msg:ChatMsg) (history:ChatMsg list) = history @ [msg] |> fixPlaceholder
    let updateQustion content (history:ChatMsg list) =  updateWith (function Question _ ->  Question content | x -> x) history
    let getQuestion (history:ChatMsg list) = history |> List.tryPick (function Question s -> Some s | _ -> None) |> Option.defaultValue ""        

