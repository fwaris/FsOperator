module Tests
open System
open Xunit
open FsOpCore

[<Fact>]
let ``add a task node to root`` () =
    let p = ONode.Seq {nodes = []; description=None}
    let t = {OTask.Create() with id = "new task"}
    let p' = p |> ONode.addNode p (ONode.Leaf t)
    let ts = p'.allTasks() |> List.filter (fun t' -> t'.id = t.id)
    Assert.True((ts.Length=1))

[<Fact>]
let ``add a task node general`` () =
    let p = ONode.Seq {nodes = []; description=None}
    let gp = ONode.Choose {nodes = []; description=None; transitionPrompt=""}
    let gp = gp |> ONode.addNode gp p
    let t = {OTask.Create() with id = "new task"}
    let gp' = gp |> ONode.addNode p (ONode.Leaf t)
    let edges = gp' |> ONode.allEdges
    let existsUnderSeq = edges |> List.exists(function (ONode.Seq _, ONode.Leaf t') -> t'.id = t.id | _ -> false)
    Assert.True(existsUnderSeq)

[<Fact>]
let ``convert leaf to seq, choose`` () =
    let t = {OTask.Create() with id = "new task"}
    let n = ONode.Leaf t
    let p = n |> ONode.convertToChoose n
    Assert.True((p.IsChoose))
    let p' = n |> ONode.convertToSeq n
    Assert.True((p'.IsSeq))
    let ps = p.allTasks() |> List.filter(fun t' -> t'.id=t.id)
    Assert.True((ps.Length = 1))
    let ps' = p'.allTasks() |> List.filter(fun t' -> t'.id=t.id)
    Assert.True((ps'.Length = 1))

[<Fact>]
let ``replace parent: root`` () =
    let t = {OTask.Create() with id = "new task"}
    let n = ONode.Leaf t
    let p = n |> ONode.replaceParent n
    Assert.True((p=n))

[<Fact>]
let ``delete node`` () =
    let t1 = {OTask.Create() with id = "new task 1"}
    let t2 = {OTask.Create() with id = "new task 2"}
    let n1 = ONode.Leaf t1
    let n2 = ONode.Leaf t2
    let p = ONode.Seq {Seq.Default with nodes = [n1; n2]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = gp |> ONode.deleteNode n1
    let edges = match gp' with Some x -> ONode.allEdges x | _ -> []
    Assert.True((edges.Length = 2))
    let (p,c) = edges.[0]    
    Assert.True((p.IsChoose && c.IsSeq))

[<Fact>]
let ``move node`` () =
    let t1 = {OTask.Create() with id = "new task 1"}
    let t2 = {OTask.Create() with id = "new task 2"}
    let n1 = ONode.Leaf t1
    let n2 = ONode.Leaf t2
    let p = ONode.Seq {Seq.Default with nodes = [n1; n2]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = gp |> ONode.moveNode n2 gp
    //note that many of the previous instances are not part of the new graph gp'
    //because nodes on the path from the root to the node n1 are reconstructed as new instances
    let edges = ONode.allEdges gp'
    let checkEdges = edges |> List.exists (fun (p,c) -> p.IsChoose && c.IsLeaf && c.displayStr().Contains("2") )
    Assert.True(checkEdges)
    let checkEdges2 = edges |> List.exists (fun (p,c) -> not (p.IsSeq && c.IsLeaf && c.displayStr().Contains("2")) )
    Assert.True(checkEdges)

[<Fact>]
let ``delete intermediate node`` () =
    let t = {OTask.Create() with id = "new task"}
    let n = ONode.Leaf t
    let p = ONode.Seq {Seq.Default with nodes = [n]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = gp |> ONode.deleteNode p
    let gp's = gp' |> Option.map _.allTasks() |> Option.defaultValue []
    Assert.True((gp's.Length = 0))

[<Fact>]
let ``replace parent: below root`` () =
    let t = {OTask.Create() with id = "new task"}
    let n = ONode.Leaf t
    let p = ONode.Seq {Seq.Default with nodes = [n]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = gp |> ONode.replaceParent n
    let edges = ONode.allEdges gp'
    Assert.True((edges.Length=1))
    let (n0,n1) = edges.[0]
    Assert.True((n0.IsChoose && n1.IsLeaf))
    let gp's = gp'.tasks() |> List.filter(fun t' -> t'.id = t.id)
    Assert.True((gp's.Length = 1))

[<Fact>]
let ``equality test`` () =
    let t1 = {OTask.Create() with id = "new task 1"}
    let t2 = {OTask.Create() with id = "new task 2"}
    let n1 = ONode.Leaf t1
    let n2 = ONode.Leaf t2
    let p = ONode.Seq {Seq.Default with nodes = [n1;n2]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = gp
    Assert.True((gp' = gp))

[<Fact>]
let ``non equality test`` () =
    let t1 = {OTask.Create() with id = "new task 1"}
    let t2 = {OTask.Create() with id = "new task 2"}
    let n1 = ONode.Leaf t1
    let n2 = ONode.Leaf t2
    let p = ONode.Seq {Seq.Default with nodes = [n1;n2]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let gp' = ONode.duplicate gp
    Assert.True((gp' <> gp))

[<Fact>]
let ``update node`` () =
    let t = {OTask.Create() with id = "old task"}
    let n = ONode.Leaf t
    let p = ONode.Seq {Seq.Default with nodes = [n]}
    let gp = ONode.Choose {Choose.Default with nodes = [p]}
    let t2 = {OTask.Create() with id = "new task"}
    let n2 = ONode.Leaf t2
    let gp' = gp |> ONode.updateNode n n2
    let ts = gp'.allTasks()
    Assert.True((ts.Length=1))
    Assert.True(ts.[0].id=t2.id)
