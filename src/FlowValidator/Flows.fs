namespace FlowValidator
open FsOpCore
open System
open System.Text.Json
open System.Threading
open FSharp.Control

module Flows =
    let readOptions = lazy(
        let opts = JsonSerializerOptions(JsonSerializerDefaults.Web, WriteIndented=true, AllowTrailingCommas=true)        
        opts)
            
    let toElement (clickable:ClickableElement) : ElemRef =
        {
            elementId = clickable.id
            inner_text = clickable.inner_text
            aria_label = clickable.aria_label
            css_classes = set clickable.classList
            tag = checkEmpty clickable.tag
            path = None
            xpath = None
        }
        
    let toParms (el: ElemRef) =
        $"""{{ 
            "elementId": {match el.elementId with Some id -> $"\"{id}\"" | None -> "null"},
            "aria_label": {match el.aria_label with Some l -> $"\"{l}\"" | None -> "null"},
            "inner_text": {match el.inner_text with Some l -> $"\"{l}\"" | None -> "null"},
            "tag": {match el.tag with Some t -> $"\"{t}\"" | None -> "null"},
            "path" : {match el.path with Some t -> $"\"{t}\"" | None -> "null"},
            "xpath" : {match el.xpath with Some t -> $"\"{t}\"" | None -> "null"},
            "classList": [{String.concat "," (el.css_classes |> Set.map (fun c -> $"\"{c}\""))}]
        }}"""
        
    let filterHiddenClickables (domS:DomSnapshot) =
        let cs =
            domS.clickables
//            |> List.filter (fun c -> c.x > 0 && c.y > 0. && c.width > 0. && c.height > 0.)            
            |> List.filter (fun c -> c.x <> 0 && c.y <> 0. && c.width > 0. && c.height > 0.)            
        { domS with clickables = cs }
                
    let findElementBox (driver:IUIDriver) (el: ElemRef) =
        let paramJson = toParms el
        let jsCallDbg = $"(function() {{ {Scripts.findElementRaw}; {Scripts.findBoundingBoxesRaw}; return findBoundingBoxes({paramJson}); }})()"
        let jsCall = $"(function() {{ {Scripts.findElement.Value}; {Scripts.findBoundingBoxes.Value}; return findBoundingBoxes({Scripts.escapeSomeChars paramJson}); }})()"
        (*task {let m = jsCallDbg
              return None}*)
        async {
            let! rslt = driver.evaluateJavaScript jsCall
            let rslt = JsonSerializer.Deserialize<string>(rslt,readOptions.Value)
            let domS = JsonSerializer.Deserialize<DomSnapshot>(rslt,readOptions.Value)
            let domS = filterHiddenClickables domS
            return Some domS            
        }
    
    let clickElement (driver:IUIDriver) (el:ElemRef) =
        let paramJson = toParms el
        let jsCallDbg = $"(function() {{ {Scripts.findElementRaw}; {Scripts.clickElementRaw}; return clickElement({paramJson}); }})()" 
        let jsCall = $"(function() {{ {Scripts.findElement.Value}; {Scripts.clickElement.Value}; return clickElement({Scripts.escapeSomeChars paramJson}); }})()"
        async {
            let! rslt = driver.evaluateJavaScript(jsCall)  
            return ()
        }
        
    let getValue (driver:IUIDriver) acc (e:Extract) = async {
        let paramJson = toParms e.ElemRef
        let jsCallDbg = $"(function() {{ {Scripts.findElementRaw}; {Scripts.getElementValueRaw}; return getElementValue({paramJson}); }})()"
        let jsCall = $"(function() {{ {Scripts.findElement.Value}; {Scripts.getElementValue.Value}; return getElementValue({Scripts.escapeSomeChars paramJson}); }})()"
        let! value = driver.evaluateJavaScript(jsCall) 
        if value <> null then
            return acc |> Map.add e.Name value
        else
            return acc                
    }        
                
    let matchElement proto candidate =
        let common = Set.intersect proto.css_classes candidate.css_classes
        proto.elementId |> Option.map (fun x -> proto.elementId ==== candidate.elementId) |> Option.defaultValue true &&
        proto.aria_label |> Option.map (fun x -> proto.aria_label ==== candidate.aria_label) |> Option.defaultValue true &&
        proto.tag |> Option.map (fun x -> proto.tag ==== candidate.tag) |> Option.defaultValue true &&
        proto.css_classes.IsEmpty || common.Count > 0
    
    let mergeValues newMap prevMap =
        (prevMap,newMap)
        ||> Map.fold (fun acc k v ->
            match acc |> Map.tryFind k with
            | Some _ -> acc
            | None   -> acc |> Map.add k v            
        )
        
    let gotoPage (driver:IUIDriver) url = async {
       return! driver.goto url
    }

    let getValues (driver:IUIDriver,extractions:Extract list) = async {
        let acc =  
            extractions
            |> AsyncSeq.ofSeq
            |> AsyncSeq.foldAsync (getValue driver) Map.empty
        return! acc 
    }
    
    let checkBox driver el = async {
        let! domS = findElementBox driver el
        return domS
        |> Option.bind(fun d->d.clickables |> List.tryHead)
        |> Option.map (fun c -> el,c)
    }
    
    let lastVisibleRef (driver:IUIDriver) (elemRefs:ElemRef list)  = 
      elemRefs
      |> AsyncSeq.ofSeq
      |> AsyncSeq.mapAsync (checkBox driver)
      |> AsyncSeq.choose id
      |> AsyncSeq.tryLast

    let clickLastVisibleElement (driver:IUIDriver) (elemRefs:ElemRef list)= async {        
          match! lastVisibleRef driver elemRefs with
          | Some (e,c) -> do! clickElement driver e
          | None -> ()
    }

    let doStep (driver:IUIDriver) (step:FlowStep) extractions = async {
        Log.info $"step {step.Desc}"
        match step with
        | Page url -> do! driver.goto url 
        | Pause _   -> do! Async.Sleep 1000                     
        | Clicks refs -> do! clickLastVisibleElement driver refs
                         do! Async.Sleep 1000      
    }
        
    let step (driver:IUIDriver) (flowrun:FlowRun) = async {
        match flowrun.ToDo with
        | step::rest -> do! doStep driver step flowrun.Flow.Extractions
                        do! Async.Sleep 1000
                        let! vs = getValues (driver,flowrun.Flow.Extractions)
                        let vs = vs |> Map.filter (fun k v -> Utility.isEmpty v |> not)
                        let vs = mergeValues vs flowrun.Values
                        Log.info $"{vs}"
                        return {flowrun with ToDo=rest; Done=step::flowrun.Done; Values=vs}
        | _ -> return flowrun                             
    }
    
