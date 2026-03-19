namespace Freetool.Domain

open System

type DomainError =
    | ValidationError of string
    | NotFound of string
    | Conflict of string
    | InvalidOperation of string

type IEntity<'TId> =
    abstract member Id: 'TId

type ExecutableHttpRequest = {
    BaseUrl: string
    UrlParameters: (string * string) list
    Headers: (string * string) list
    Body: (string * string) list
    HttpMethod: string
    UseJsonBody: bool
}

// CLIMutable for EntityFramework
[<CLIMutable>]
type RunInputValue = { Title: string; Value: string }

module DomainValidation =
    let checkKeyValueConflicts
        (resourceValues: (string * string) list option)
        (appValues: (string * string) list option)
        (conflictType: string)
        : string option =

        match resourceValues, appValues with
        | Some resValues, Some appValues ->
            let resourceKeys = resValues |> List.map fst |> Set.ofList
            let appKeys = appValues |> List.map fst |> Set.ofList
            let conflicts = Set.intersect resourceKeys appKeys |> Set.toList

            if not conflicts.IsEmpty then
                let conflictList = String.concat ", " conflicts
                Some $"{conflictType}: {conflictList}"
            else
                None
        | _ -> None