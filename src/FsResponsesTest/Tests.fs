module Tests
open FsResponses
open System
open System.Threading.Tasks
open Xunit

let dmp (s:string) =
    System.Diagnostics.Debug.WriteLine(s)

let runT (t:Task<'t>) = t.Result

let image = lazy(System.Text.Json.JsonSerializer.Deserialize<string>(IO.File.ReadAllText("TestImage.txt")))

let createRequest() = 
    {Request.Default with
        input = 
            [
                IOitem.Message 
                    {Message.Default with 
                        content = 
                            [
                                Content.Input_text {| text = "Describe the image"|} 
                                Content.Input_image {| image_url = image.Value|}
                            ]}
            ]
        store = false
        model=Models.gpt_41_nano
        metadata = ["CORR_ID","1"] |> Map.ofList |> Some
    }

[<Fact>]
let ``Create a response`` () = task {            
    let req = createRequest()
    let! resp = Api.create req (Api.defaultClient())
    let text = FsResponses.RUtils.outputText resp
    dmp text
    let corrId = resp.metadata |> Option.bind (Map.tryFind "CORR_ID")
    Assert.True((corrId = Some "1"))
}

[<Fact>]
let ``List stored items`` () =          
    let req = {createRequest() with store=true}          //store state on server
    let resp = Api.create req (Api.defaultClient()) |> runT
    let msgIds1 = resp.output |> List.choose (function
        | IOitem.Message m -> m.id
        | _ -> None)
    let req2 = 
        {Request.Default with 
            previous_response_id = Some resp.id //'chat history' is on server so reference it
            input = 
                [
                    IOitem.Message 
                        {Message.Default with 
                            content = [Content.Input_text {|text = "Is there a search box visible in the image"|}]
                        }
                ]
            store = true //need to set to true to keep storing the history on the server
            metadata = ["CORR_ID","2"] |> Map.ofList |> Some
        
        }
    let resp2 = Api.create req2 (Api.defaultClient()) |> runT
    let text = FsResponses.RUtils.outputText resp2
    dmp text   
    let expectedMsgIds = set msgIds1
    let corrId = resp2.metadata |> Option.bind (Map.tryFind "CORR_ID")
    let listResp = Api.list {ListRequest.Create resp2.id with order = Some Asc } (Api.defaultClient()) |> runT
    dmp(sprintf "List response: %A" listResp)    
    let listIds = listResp.data |> List.choose (function| IOitem.Message m -> m.id | _ -> None) |> set
    let accountedFor = Set.intersect expectedMsgIds listIds
    Assert.True((corrId = Some "2"))
    Assert.True((expectedMsgIds = accountedFor))

[<Fact>]
let ``Create and delete a response`` () = task {            
    let req = {createRequest() with store = true} //store state on server
    let! resp = Api.create req (Api.defaultClient())
    let text = FsResponses.RUtils.outputText resp
    dmp text
    let corrId = resp.metadata |> Option.bind (Map.tryFind "CORR_ID")
    let! deleteResult = Api.delete resp.id (Api.defaultClient()) 
    Assert.True((corrId = Some "1"))
    Assert.True(deleteResult.deleted)
}

