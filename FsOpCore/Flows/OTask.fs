namespace FsOpCore
open System.Collections.Generic

///represents the target computer environment (browser url or windows exe) for a task
type OTaskTarget =
    | OProcess of string*string option
    | OLink of string
    with member this.TargetString() =
            match this with
            | OProcess (a,b) -> $"{a} {b}"
            | OLink s -> s

///definition of a single unit of work in a plan
type OTask = {
    id          : string
    target      : OTaskTarget
    description : string
    cua         : string option
    reasoner    : string option
    voice       : string option
    tools       : FsResponses.Function list
    allowedSec  : int
}
    with
        ///creates a new empty task with unique id assigned
        static member Create() =  {
                id = newId()
                target = OLink ""
                description = ""
                cua = None
                reasoner = None
                voice = None
                tools = []
                allowedSec = 60*10
            }

///Represents a 'choice' task node. One task of the available task is to be selected.
and Choose = {
    ///<summary>
    ///Prompt template to decide which one of the available tasks to run next.<br />
    ///Must be a SK prompt template with the following variable slots:<br />
    /// - {{taskDescriptions}} - will be used to supply the subtask descriptions<br />
    /// - {{context}} - will be filled with message history from previous task
    ///</summary>
    transitionPrompt : string

    nodes     : ONode list

    ///if the Choose node is a child of another Choose node
    ///then this description is used in the transition decision
    description:string option
}
with static member Default = {nodes=[]; transitionPrompt=""; description=None}

///represents a sequence of task nodes that are to be completed in order
and Seq = {
    nodes : ONode list

    ///if the All node is a child of a Choose node
    ///then this description is used in the transition decision
    description:string option
}
with 
    static member Default = {nodes=[]; description=None}

///task node tree structure
and [<RequireQualifiedAccess; ReferenceEquality >] ONode =
    | Leaf of OTask      //Leaf node containing the task
    | Choose of Choose  //execute one of many sub nodes - based on LLM decision involving transition prompt
    | Seq of Seq        //execute all sub nodes in sequence
    with
        member this.displayStr() = match this with 
                                   | ONode.Choose c -> c.description |> Option.defaultValue ""
                                   | ONode.Seq c -> c.description |> Option.defaultValue ""
                                   | ONode.Leaf t -> t.id

        member this.allTasks() =
            let rec loop (visited:HashSet<ONode>,acc:OTask list) (p:ONode) = //need to protect against possible cycles
                if visited.Contains p then
                    (visited,acc)
                else
                    visited.Add p |> ignore
                    match p with
                    | ONode.Choose c -> ((visited,acc),c.nodes) ||> List.fold loop                                        
                    | ONode.Seq s    -> ((visited,acc),s.nodes) ||> List.fold loop
                    | ONode.Leaf t   -> (visited,t::acc)
            loop (HashSet(),[]) this |> snd

        member this.tasks() =
            match this with
            | ONode.Choose c -> c.nodes |> List.choose (function ONode.Leaf t -> Some t | _ -> None)
            | ONode.Seq s    -> s.nodes |> List.choose (function ONode.Leaf t -> Some t | _ -> None)
            | ONode.Leaf t   -> [t]

module ONode =
    ///Add a new node n
    ///under the parent p
    let addNode (parent:ONode) (n:ONode) (root:ONode) =
        let rec loop (visited:HashSet<ONode>) (c:ONode) =
            if visited.Contains c then 
                c
            else 
                visited.Add c |> ignore
                if parent = c then
                    match c with
                    | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes @ [n]}
                    | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes @ [n]}
                    | ONode.Leaf l -> failwith "a leaf node cannot be a parent"
                else
                    match c with
                    | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map (loop visited)}
                    | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map (loop visited)}
                    | t -> t
        loop (HashSet()) root

    ///Delete node under root. Fails if node does not exist. Returns None if root itself is deleted.
    let deleteNode (n:ONode) (root:ONode) =
        let rec loop (visited:HashSet<_>,p:ONode option) (c:ONode) =
            if visited.Contains c 
                then Some c
            else 
                visited.Add c |> ignore
                match c=n with
                | true      -> None
                | false     -> match c with
                               | ONode.Seq s -> Some(ONode.Seq {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))})
                               | ONode.Choose s -> Some(ONode.Choose {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))})
                               | ONode.Leaf _ -> Some c
        loop (HashSet(),None) root

    type Dir = Up | Down

    let private reorder dir (n:ONode) (ns:ONode list) =
        let i = ns |> List.tryFindIndex (fun n' -> n' = n)
        match i with
        | None -> ns
        | Some i ->
            match dir with
            | Up when i <> 0              -> ns |> List.removeAt i |> List.insertAt (i-1) n
            | Up                          -> ns
            | Down when i < ns.Length - 1 -> ns |> List.removeAt i |> List.insertAt (i+1) n
            | Down                        -> ns

    let reorderNode dir (n:ONode) (parent:ONode) =
        match parent with
        | ONode.Seq s -> ONode.Seq {s with nodes = reorder dir n s.nodes}
        | ONode.Choose s -> ONode.Choose {s with nodes = reorder dir n s.nodes}
        | n -> n

    let rec duplicate (n:ONode) =
        match n with
        | ONode.Leaf t -> ONode.Leaf t
        | ONode.Seq s -> ONode.Seq {s with nodes = s.nodes |> List.map duplicate}
        | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map duplicate}

    let convertToSeq (n:ONode) (root:ONode) =
        let rec loop (c:ONode) =
            if c = n then
                match c with
                | ONode.Leaf _   -> ONode.Seq {nodes=[n]; description=None}
                | ONode.Seq _    -> n
                | ONode.Choose c -> ONode.Seq {nodes=c.nodes; description=c.description}
            else
                match c with
                | ONode.Seq s    -> ONode.Seq {s with nodes = s.nodes |> List.map loop}
                | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map loop}
                | t -> t
        loop root

    let convertToChoose (n:ONode) (root:ONode) =
        let rec loop (c:ONode) =
            if c = n then
                match c with
                | ONode.Leaf _   -> ONode.Choose {nodes=[n]; description=None; transitionPrompt=""}
                | ONode.Seq s    -> ONode.Choose {nodes=s.nodes; description=s.description; transitionPrompt=""}
                | ONode.Choose _ -> n
            else
                match c with
                | ONode.Seq s    -> ONode.Seq {s with nodes = s.nodes |> List.map loop}
                | ONode.Choose s -> ONode.Choose {s with nodes = s.nodes |> List.map loop}
                | t -> t
        loop root

    let replaceParent (n:ONode) (root:ONode) =
        let rec loop (gp:ONode option) (p:ONode option) (c:ONode) =
            match gp, p, c=n with
            | None, None, true -> Choice1Of2(c) //already root
            | None, Some _, true -> Choice1Of2(c) //just under root so replace root
            | Some gp, Some p, true -> Choice1Of2 c
            | _, p, false -> //recurse down
                match c with
                | ONode.Seq s as pn    -> let ns = s.nodes |> List.map (loop p (Some pn))
                                          let rep = ns |> List.tryPick(function Choice1Of2 n -> Some n | _ -> None)
                                          match rep with 
                                          | Some n -> Choice2Of2 n  //this node is parent, it was replaced by a child
                                          | _      -> let n = ONode.Seq {s with nodes = ns |> List.choose(function Choice2Of2 n -> Some n | _ -> None)}
                                                      Choice2Of2 n
                | ONode.Choose s as pn -> let ns = s.nodes |> List.map (loop p (Some pn))
                                          let rep = ns |> List.tryPick(function Choice1Of2 n -> Some n | _ -> None)
                                          match rep with 
                                          | Some n -> Choice2Of2 n  //this node is parent it was replaced by a child
                                          | _      -> let n = ONode.Choose {s with nodes = ns |> List.choose(function Choice2Of2 n -> Some n | _ -> None)}
                                                      Choice2Of2 n
                | _ -> failwith "not expected"
            | _,_,_ -> failwith "not expected"
        match loop None None root with 
        | Choice1Of2 n | Choice2Of2 n -> n

    ///All parent child relations
    let allEdges (root:ONode) =
        let rec loop (visited:HashSet<ONode>,acc:(ONode*ONode) list) (p:ONode) =
            if visited.Contains p then
                (visited,acc)
            else
                visited.Add p |> ignore
                match p with
                | ONode.Choose c -> let acc = acc @ (c.nodes |> List.map (fun x -> (p,x)))
                                    ((visited,acc),c.nodes) ||> List.fold loop
                | ONode.Seq s    -> let acc = acc @ (s.nodes |> List.map (fun x -> (p,x)))
                                    ((visited,acc),s.nodes) ||> List.fold loop
                | ONode.Leaf l   -> (visited,acc)
        loop (HashSet(),[]) root |> snd

    ///For internal use. Deletes a node but maintains a dictionary that maps old to new instances for all changed nodes.
    ///Need this to 'move' a node from one parent to another because deleting a node can re-create all nodes on the path to the deleted node.
    ///If the new parent happens to be on this path then the old instance is now not part of the new tree.
    let _deleteNode (n:ONode) (root:ONode) =
        let tracker = new Dictionary<ONode,ONode>()
        let rec loop (visited:HashSet<_>,p:ONode option) (c:ONode) =
            if visited.Contains c 
                then Some c
            else 
                visited.Add c |> ignore
                match c=n with
                | true      -> None
                | false     -> match c with
                               | ONode.Seq s -> let c' = ONode.Seq {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))}
                                                tracker.Add(c,c')
                                                Some c'
                               | ONode.Choose s -> let c' = ONode.Choose {s with nodes = s.nodes |> List.choose (loop (visited,(Some c)))}
                                                   tracker.Add(c,c')
                                                   Some c'
                               | ONode.Leaf _ -> Some c
        let root = loop (HashSet(),None) root
        root |> Option.map(fun r -> r,tracker)

    let moveNode (n:ONode) (newParent:ONode) (root:ONode) = 
        match _deleteNode n root with 
        | None -> root
        | Some (root,tracker) ->
            let newParent' = match tracker.TryGetValue(newParent) with | true, np -> np | _ -> newParent
            addNode newParent' n root
