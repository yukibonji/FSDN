﻿namespace FSDN

open System.Collections.Generic
open System.Runtime.Serialization
open Microsoft.FSharp.Reflection
open FSharpApiSearch

module SearchOptionLiteral =

  [<Literal>]
  let RespectNameDifference = "respect_name_difference"

  [<Literal>]
  let GreedyMatching = "greedy_matching"

  [<Literal>]
  let IgnoreParamStyle = "ignore_param_style"

[<DataContract>]
type ApiName = {
  [<field: DataMember(Name = "id")>]
  Id: string
  [<field: DataMember(Name = "class_name")>]
  Class: string
  [<field: DataMember(Name = "namespace")>]
  Namespace: string
}

[<DataContract>]
type FSharpApi = {
  [<field: DataMember(Name = "name")>]
  Name: ApiName
  [<field: DataMember(Name = "kind")>]
  Kind: string
  [<field: DataMember(Name = "signature")>]
  Signature: string
  [<field: DataMember(Name = "type_constraints")>]
  TypeConstraints: string
  [<field: DataMember(Name = "assembly")>]
  Assembly: string
  [<field: DataMember(Name = "xml_doc")>]
  XmlDoc: string
}

[<DataContract>]
type SearchResult = {
  [<field: DataMember(Name = "distance")>]
  Distance: int
  [<field: DataMember(Name = "api")>]
  Api: FSharpApi
}

[<DataContract>]
type SearchOptions = {
  [<field: DataMember(Name = SearchOptionLiteral.RespectNameDifference)>]
  RespectNameDifference: string
  [<field: DataMember(Name = SearchOptionLiteral.GreedyMatching)>]
  GreedyMatching: string
  [<field: DataMember(Name = SearchOptionLiteral.IgnoreParamStyle)>]
  IgnoreParamStyle: string
}

[<DataContract>]
type SearchInformation = {
  [<field: DataMember(Name = "target_assemblies")>]
  Targets: string []
  [<field: DataMember(Name = "search_options")>]
  RawOptions: SearchOptions
  [<field: DataMember(Name = "query")>]
  Query: string
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharpApi =

  [<RequireQualifiedAccess>]
  module Name =

    let private printDisplayName = function
    | [] -> "<empty>"
    | ns ->
      let print (x: NameItem) =
        match x.GenericParametersForDisplay with
        | [] -> x.FSharpName
        | args -> sprintf "%s<%s>" x.FSharpName (args |> List.map (sprintf "'%s") |> String.concat ", ")
      ns |> Seq.map print |> Seq.rev |> String.concat "."

    let id (name: Name) =
      match name with
      | LoadingName _ -> failwith "LoadingName only use to generate database."
      | DisplayName xs -> printDisplayName (List.truncate 1 xs)

    let ``namespace`` (name: Name) =
      match name with
      | LoadingName _ -> failwith "LoadingName only use to generate database."
      | DisplayName xs -> printDisplayName (List.skip 2 xs)

    let className (name: Name) =
      match name with
      | LoadingName _ -> failwith "LoadingName only use to generate database."
      | DisplayName xs -> printDisplayName (xs |> List.skip 1 |> List.truncate 1)

  let toSerializable (results: FSharpApiSearch.Result seq) =
    {
      Values =
        results
        |> Seq.map (fun result ->
          {
            Distance = result.Distance
            Api =
              {
                Name =
                  {
                    Id = Name.id result.Api.Name
                    Namespace = Name.``namespace`` result.Api.Name
                    Class = Name.className result.Api.Name
                  }
                Kind = result.Api.PrintKind()
                Signature = result.Api.PrintSignature()
                TypeConstraints =
                  if not <| result.Api.TypeConstraints.IsEmpty then result.Api.PrintTypeConstraints()
                  else ""
                Assembly = result.AssemblyName
                XmlDoc =
                  match result.Api.Document with
                  | Some doc -> doc
                  | None -> ""
              }
          })
        |> Seq.toArray
    }

  module OptionStatus =

    let tryParse = function
    | "enabled" -> Some Enabled
    | "disabled" -> Some Disabled
    | _ -> None

    let parseOrDefault defaultValue value =
      match tryParse value with
      | Some value -> value
      | None -> defaultValue

  module SearchOptions =

    open Suave
    open SearchOptionLiteral

    let parse info =
      let updateRespectNameDifference value (opt: FSharpApiSearch.SearchOptions) =
        { opt with RespectNameDifference = OptionStatus.parseOrDefault SearchOptions.defaultOptions.RespectNameDifference value }
      let updateGreedyMatching value (opt: FSharpApiSearch.SearchOptions) =
        { opt with GreedyMatching = OptionStatus.parseOrDefault SearchOptions.defaultOptions.GreedyMatching value }
      let updateIgnoreParamStyle value (opt: FSharpApiSearch.SearchOptions) =
        { opt with IgnoreParameterStyle = OptionStatus.parseOrDefault SearchOptions.defaultOptions.IgnoreParameterStyle value }
      SearchOptions.defaultOptions
      |> updateRespectNameDifference info.RawOptions.RespectNameDifference
      |> updateGreedyMatching info.RawOptions.GreedyMatching
      |> updateIgnoreParamStyle info.RawOptions.IgnoreParamStyle

  let trySearch database info =
    let client = FSharpApiSearchClient(info.Targets, database)
    try
      client.Search(info.Query, SearchOptions.parse info)
      |> Seq.sortBy (fun x -> (x.Distance, x.Api.Name.Print()))
      |> Choice1Of2
    with e -> Choice2Of2 e
