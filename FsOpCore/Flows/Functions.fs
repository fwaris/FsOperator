namespace FsOpCore.Functions
open FsOpCore
open Microsoft.SemanticKernel
open System.ComponentModel
open System.Text.Json

///plugin that provides navigation related functions
type FsOpNavigator() =
    let startUrl:Ref<string> = ref Unchecked.defaultof<_>
    let driver:Ref<IUIDriver> = ref Unchecked.defaultof<_>

    member this.SetStartUrl(url:string) = 
        Log.info $"{nameof this.SetStartUrl} {url}"
        startUrl.Value <- url

    member this.SetDriver(drv:IUIDriver) =
        Log.info $"{nameof this.SetDriver} {drv.GetType().Name}"
        driver.Value <- drv

    [<KernelFunction("home")>]
    [<Description("Load initial task page")>]
    member this.home() =
        let comp = async {
            Log.info $"{nameof this.home}"
            if startUrl.Value <> Unchecked.defaultof<_> && driver.Value <> Unchecked.defaultof<_> then 
                do! driver.Value.start startUrl.Value
                return "home page loaded"
            else
                return "a home page url not found"
        }
        Async.StartAsTask comp

    [<KernelFunction("get_current_url")>]
    [<Description("Get the current URL of the browser")>]
    member this.get_current_url() =
        let comp = async {
            Log.info $"{nameof this.get_current_url}"
            let noUrl = "unable to get url"
            if driver.Value <> Unchecked.defaultof<_> then 
                match! driver.Value.url() with
                | Some url -> return url
                | None     -> return noUrl
            else
                return noUrl
        }
        Async.StartAsTask comp

///semantic kernel 'plugin' class that implements memory functions
type FsOpMemory() =
    let mutable bag = Map.empty
    static member statefile = lazy(homePath.Value @@ "memory.json")

    static member serOpts = lazy(
        let opts =JsonSerializerOptions()
        opts.WriteIndented <- true
        opts)

    member this.SetMemory(m) = bag <- m

    static member LoadState() =
        try
            if System.IO.File.Exists(FsOpMemory.statefile.Value) then
                use str = System.IO.File.OpenRead(FsOpMemory.statefile.Value)
                let map = System.Text.Json.JsonSerializer.Deserialize<Map<string,string list>>(str)
                let mem = new FsOpMemory()
                mem.SetMemory(map)
                mem
            else
                new FsOpMemory()
        with ex ->
            Log.exn(ex, nameof FsOpMemory.LoadState)
            new FsOpMemory()

    static member Serialize<'t>(o:'t) = JsonSerializer.Serialize(o,options=FsOpMemory.serOpts.Value)

    static member private _SaveState(map:Map<string,string list>) =
        try
            use str = System.IO.File.Create FsOpMemory.statefile.Value
            JsonSerializer.Serialize(str,map, options=FsOpMemory.serOpts.Value)
        with ex ->
            Log.exn(ex,nameof FsOpMemory._SaveState)

    [<KernelFunction("memory_save")>]
    [<Description("Save a key-value pair for later retrieval")>]
    member this.memory_save(key:string, value:string) =
        Log.info $"{nameof this.memory_save}:{key} = {value}"
        lock bag (fun _ -> 
            bag <-
                bag 
                |> Map.tryFind key 
                |> Option.map (fun vs -> bag |> Map.add key (List.distinct (value::vs)))
                |> Option.defaultWith (fun _ -> bag |> Map.add key [value])
            FsOpMemory._SaveState(bag)
        )
        "saved"

    [<KernelFunction("memory_get_all")>]
    [<Description("Retrieve all key value pairs saved in memory")>]
    member this.memory_get_all() =
        Log.info (nameof this.memory_get_all)
        FsOpMemory.Serialize(bag)

    [<KernelFunction("memory_get_all_keys")>]
    [<Description("retrieve all keys in the memory store ")>]
    member this.memory_get_all_keys() =
        let ks = Map.keys bag |> Seq.toList
        Log.info $"{nameof this.memory_get_all_keys}: {ks}"
        FsOpMemory.Serialize(ks)

    [<KernelFunction("memory_get_value")>]
    [<Description("retrieve a value for the given key")>]
    member this.memory_get_value(key:string) =
        let v = bag |> Map.tryFind key
        Log.info $"{nameof this.memory_get_value} {key} = {v}"
        FsOpMemory.Serialize(v)

    member this.getMemory() = bag


type VoiceFuncImpl = {
    gotoUrl : string -> Async<unit>
    addGuidance : string -> Async<unit>
}

///Semantic kernel 'plugin' class that implements functions required by voice assistant
type FsOpVoice() = //need parameterless constructor so SK can extract function tool defs
    let voiceAsstFuncs = ref Unchecked.defaultof<_>

    member this.SetFunctions(va:VoiceFuncImpl) = voiceAsstFuncs.Value <- va

    [<KernelFunction("voice_gotoUrl")>]
    [<Description("Ask the agent to go to a specific URL")>]
    member this.gotoUrl(url:string) = 
        let comp = async {
            try
                do! voiceAsstFuncs.Value.gotoUrl(url)
                return $"gotoUrl {url} invoked"                
            with ex ->
                Log.exn(ex, nameof this.gotoUrl)
                return $"gotoUrl {url} failed: {ex.Message}"
        }
        Async.StartAsTask comp
    
    [<KernelFunction("voice_addGuidance")>]
    [<Description("Give agent additional guidance")>]
    member this.addGuidance(guidance:string) = 
        let comp = async {
            try
                do! voiceAsstFuncs.Value.addGuidance(guidance)
                return "guidance added"
            with ex ->
                Log.exn(ex, nameof this.addGuidance)
                return $"addGuidance failed: {ex.Message}"
        }
        Async.StartAsTask comp
