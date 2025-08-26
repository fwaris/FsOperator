module ModelPricing

type ModelPrice = {
    StableName: string
    SnapshotName: string
    Input: float option
    CachedInput: float option
    Output: float option
}

let prices : ModelPrice list = [
    { StableName = "gpt-4.1";           SnapshotName = "gpt-4.1-2025-04-14";           Input = Some 2.00;  CachedInput = Some 0.50;   Output = Some 8.00 }
    { StableName = "gpt-4.1-mini";      SnapshotName = "gpt-4.1-mini-2025-04-14";      Input = Some 0.40;  CachedInput = Some 0.10;   Output = Some 1.60 }
    { StableName = "gpt-4.1-nano";      SnapshotName = "gpt-4.1-nano-2025-04-14";      Input = Some 0.10;  CachedInput = Some 0.025;  Output = Some 0.40 }
    { StableName = "gpt-4.5-preview";   SnapshotName = "gpt-4.5-preview-2025-02-27";   Input = Some 75.00; CachedInput = Some 37.50;  Output = Some 150.00 }
    { StableName = "gpt-4o";            SnapshotName = "gpt-4o-2024-08-06";            Input = Some 2.50;  CachedInput = Some 1.25;   Output = Some 10.00 }
    { StableName = "gpt-4o-audio-preview"; SnapshotName = "gpt-4o-audio-preview-2024-12-17"; Input = Some 2.50; CachedInput = None; Output = Some 10.00 }
    { StableName = "gpt-4o-realtime-preview"; SnapshotName = "gpt-4o-realtime-preview-2025-06-03"; Input = Some 5.00; CachedInput = Some 2.50; Output = Some 20.00 }
    { StableName = "gpt-4o-mini";       SnapshotName = "gpt-4o-mini-2024-07-18";       Input = Some 0.15;  CachedInput = Some 0.075;  Output = Some 0.60 }
    { StableName = "gpt-4o-mini-audio-preview"; SnapshotName = "gpt-4o-mini-audio-preview-2024-12-17"; Input = Some 0.15; CachedInput = None; Output = Some 0.60 }
    { StableName = "gpt-4o-mini-realtime-preview"; SnapshotName = "gpt-4o-mini-realtime-preview-2024-12-17"; Input = Some 0.60; CachedInput = Some 0.30; Output = Some 2.40 }
    { StableName = "o1";                SnapshotName = "o1-2024-12-17";                Input = Some 15.00; CachedInput = Some 7.50;   Output = Some 60.00 }
    { StableName = "o1-pro";            SnapshotName = "o1-pro-2025-03-19";            Input = Some 150.00;CachedInput = None;        Output = Some 600.00 }
    { StableName = "o3-pro";            SnapshotName = "o3-pro-2025-06-10";            Input = Some 20.00; CachedInput = None;        Output = Some 80.00 }
    { StableName = "o3";                SnapshotName = "o3-2025-04-16";                Input = Some 2.00;  CachedInput = Some 0.50;   Output = Some 8.00 }
    { StableName = "o3-deep-research";  SnapshotName = "o3-deep-research-2025-06-26";  Input = Some 10.00; CachedInput = Some 2.50;   Output = Some 40.00 }
    { StableName = "o4-mini";           SnapshotName = "o4-mini-2025-04-16";           Input = Some 1.10;  CachedInput = Some 0.275;  Output = Some 4.40 }
    { StableName = "o4-mini-deep-research"; SnapshotName = "o4-mini-deep-research-2025-06-26"; Input = Some 2.00; CachedInput = Some 0.50; Output = Some 8.00 }
    { StableName = "o3-mini";           SnapshotName = "o3-mini-2025-01-31";           Input = Some 1.10;  CachedInput = Some 0.55;   Output = Some 4.40 }
    { StableName = "o1-mini";           SnapshotName = "o1-mini-2024-09-12";           Input = Some 1.10;  CachedInput = Some 0.55;   Output = Some 4.40 }
    { StableName = "codex-mini-latest"; SnapshotName = "codex-mini-latest";            Input = Some 1.50;  CachedInput = Some 0.375;  Output = Some 6.00 }
    { StableName = "gpt-4o-mini-search-preview"; SnapshotName = "gpt-4o-mini-search-preview-2025-03-11"; Input = Some 0.15; CachedInput = None; Output = Some 0.60 }
    { StableName = "gpt-4o-search-preview"; SnapshotName = "gpt-4o-search-preview-2025-03-11"; Input = Some 2.50; CachedInput = None; Output = Some 10.00 }
    { StableName = "computer-use-preview"; SnapshotName = "computer-use-preview-2025-03-11"; Input = Some 3.00; CachedInput = None; Output = Some 12.00 }
    { StableName = "gpt-image-1";       SnapshotName = "gpt-image-1";                  Input = Some 5.00;  CachedInput = Some 1.25;   Output = None }
    { StableName = "gpt-5"; SnapshotName = "gpt-5-2025-08-07"; Input = Some 1.50; CachedInput = Some 0.125; Output = Some 10.00 }
    { StableName = "gpt-5-mini"; SnapshotName = "gpt-5-mini"; Input = Some 0.25; CachedInput = Some 0.025; Output = Some 2.00 }
]

let priceMap = lazy((prices |> List.map (fun m -> m.StableName.ToLower(),m )) @ ( prices |> List.map (fun m -> m.SnapshotName.ToLower(), m)) |> Map.ofList)

let calcPrice (modelId:string,u:FsResponses.Usage) = 
    let modelId = modelId.ToLower()
    priceMap.Value 
        |> Map.toSeq
        |> Seq.tryFind(fun (k,v) -> modelId.Contains(k))
        |> Option.map snd
        //|> Map.tryFind modelId 
        |> Option.bind(fun m -> 
            m.Input 
            |> Option.map(fun pi -> (float u.input_tokens / 1_000_000.) * pi) 
            |> Option.bind(fun ci -> m.Output |> Option.map( fun po -> ci +  po * (float u.output_tokens / 1_000_000.))))
        |> Option.defaultValue -1.
