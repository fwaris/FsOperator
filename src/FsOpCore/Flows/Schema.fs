module Schema
open System
open System.Reflection
open Microsoft.SemanticKernel
open FsResponses

// --- Minimal defaults -------------------------------------------------------
// let prop() =
//   { ``type``="object"; description=""; properties=Map.empty; required=[]
//     items=Unchecked.defaultof<_>; enum=[]; additionalProperties=false }

let paramsDefault() =
  { ``type``="object"; properties=Map.empty; required=[]; additionalProperties=false}

// --- Tiny helpers -----------------------------------------------------------
let isFSharpOption (t: Type) =
  t.IsGenericType && t.GetGenericTypeDefinition().FullName = "Microsoft.FSharp.Core.FSharpOption`1"

let unwrapOption (t: Type) = t.GetGenericArguments().[0]

let isNullableStruct (t: Type) =
  Nullable.GetUnderlyingType t |> isNull |> not

let unwrapNullable (t: Type) =
  Nullable.GetUnderlyingType t

let isSeqLike (t: Type) =
  t.IsArray ||
  t.GetInterfaces()
   |> Array.exists (fun i -> i.IsGenericType &&
                             i.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IEnumerable<_>>)

let tryElemType (t: Type) =
  if t.IsArray then Some (t.GetElementType())
  else
    t.GetInterfaces()
    |> Array.tryPick (fun i ->
        if i.IsGenericType &&
           i.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IEnumerable<_>> then
          Some (i.GetGenericArguments().[0]) else None)

let isDictStringKey (t: Type) =
  t.GetInterfaces()
  |> Array.exists (fun i ->
      i.IsGenericType &&
      let g = i.GetGenericTypeDefinition()
      g = typedefof<System.Collections.Generic.IDictionary<_,_>> &&
      i.GetGenericArguments().[0] = typeof<string>)

let tryDictValueType (t: Type) =
  t.GetInterfaces()
  |> Array.tryPick (fun i ->
      if i.IsGenericType &&
         i.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IDictionary<_,_>> &&
         i.GetGenericArguments().[0] = typeof<string> then
        Some (i.GetGenericArguments().[1])
      else None)

let basicType (t: Type) =
  if   t = typeof<string> then Some (JsProperty.String {description=None; enum=None})
  elif t = typeof<bool>   then Some (JsProperty.Boolean {description=None})
  elif t = typeof<byte> || t = typeof<int16> || t = typeof<int> || t = typeof<int64> then Some (JsProperty.Integer {description=None})
  elif t = typeof<float> || t = typeof<double> || t = typeof<decimal> then Some (JsProperty.Number {description=None}) // number is string in JSON schema
  elif t = typeof<DateTime> || t = typeof<DateTimeOffset> || t = typeof<Guid> then Some (JsProperty.String {description=None; enum=None}) // date/time/guid are strings in JSON schema
  else None

// --- Core: minimal recursive schema builder --------------------------------
let rec schemaForType (t0: Type) : JsProperty =
  // unwrap option/nullable for schema (optional-ness handled by "required" at parent)
  let t =
    if isFSharpOption t0 then unwrapOption t0
    elif isNullableStruct t0 then unwrapNullable t0
    else t0

  // enums → string + enum
  if t.IsEnum then
    JsProperty.String {description=None; enum = Some (Enum.GetNames t |> Array.toList)}
    //{ prop() with ``type``="string"; enum = (Enum.GetNames t |> Array.toList) }

  // primitives
  elif basicType t |> Option.isSome then (basicType t).Value

  // arrays / IEnumerable<T>
  elif isSeqLike t then
    let itemT = tryElemType t |> Option.defaultValue typeof<obj>
    JsProperty.Array {description=None; items = schemaForType itemT}
    //{ prop() with ``type``="array"; items=(schemaForType itemT) }

//   // Dictionary<string, T> → object with additionalProperties: <T schema>
//   elif isDictStringKey t then
//     let v = tryDictValueType t |> Option.defaultValue typeof<obj>
//     Property.Object {|description=""; properties=Map.empty; required=[]; additionalProperties = true|}
//     //{ prop() with ``type``="object" }

  // object: reflect public getters
  else
    let ps =
      t.GetProperties(BindingFlags.Public ||| BindingFlags.Instance)
      |> Array.filter (fun p -> p.CanRead && p.GetMethod.IsPublic)
    let props =
      ps |> Array.map (fun p -> p.Name, schemaForType p.PropertyType) |> Map.ofArray
    // minimal required rule:
    // value types (not Nullable<>) are required; reference types are optional
    let required =
      ps
      // |> Array.filter (fun p ->
      //      p.PropertyType.IsValueType && not (isNullableStruct p.PropertyType))
      |> Array.map (fun p -> p.Name)
      |> Array.toList

    JsProperty.Object {description=None; properties=props; required=required; additionalProperties=false}
    // { prop() with
    //     ``type``="object"
    //     properties=props
    //     required = required
    //     additionalProperties = false }

// --- Adapter to your metadata ----------------------------------------------
// You already have: KernelFunctionMetadata, KernelParameterMetadata, etc.
// Here we assume minimal members used below.

let checkEmpty (s:string) =
  if String.IsNullOrWhiteSpace s then None else Some s

let propertyFor (p: KernelParameterMetadata) =
  let s = schemaForType p.ParameterType
  match checkEmpty p.Description with
  | None -> s
  | Some d -> match s with 
              | JsProperty.Integer _ -> JsProperty.Integer {description=Some d}
              | JsProperty.Number _ -> JsProperty.Number {description=Some d}
              | JsProperty.String _ -> JsProperty.String {description=Some d; enum=None}
              | JsProperty.Boolean _ -> JsProperty.Boolean {description=Some d}
              | JsProperty.Array a -> JsProperty.Array {a with description=Some d}
              | JsProperty.Object o -> JsProperty.Object {o with description=Some d}

let toFunctionTool (m: KernelFunctionMetadata) : Function =
    let props =
        m.Parameters
        |> Seq.map (fun p -> p.Name, propertyFor p)
        |> Map.ofSeq

    let requiredTop =
        m.Parameters
        |> Seq.map (fun p ->  p.Name)
        |> Seq.toList

    { 
        name = m.Name
        description = m.Description
        parameters =
            { paramsDefault() with
                properties = props
                required = requiredTop } 
        strict = true
    }
