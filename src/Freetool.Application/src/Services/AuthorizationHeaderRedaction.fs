namespace Freetool.Application.Services

open System
open Freetool.Domain.ValueObjects

module AuthorizationHeaderRedaction =
    [<Literal>]
    let RedactedPlaceholder = "---- redacted ----"

    let isAuthorizationHeaderKey (key: string) : bool =
        key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)

    let redactAuthorizationHeaderValue (value: string) : string =
        if value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) then
            $"Bearer {RedactedPlaceholder}"
        elif value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) then
            $"Basic {RedactedPlaceholder}"
        else
            RedactedPlaceholder

    let redactKeyValuePairs (headers: KeyValuePair list) : KeyValuePair list =
        headers
        |> List.map (fun header ->
            if isAuthorizationHeaderKey header.Key then
                let redactedValue = redactAuthorizationHeaderValue header.Value

                match KeyValuePair.Create(header.Key, redactedValue) with
                | Ok redacted -> redacted
                | Error _ -> header
            else
                header)

    let redactTupleHeaders (headers: (string * string) list) : (string * string) list =
        headers
        |> List.map (fun (key, value) ->
            if isAuthorizationHeaderKey key then
                (key, redactAuthorizationHeaderValue value)
            else
                (key, value))

    let isRedactedAuthorizationValue (value: string) : bool =
        value.IndexOf(RedactedPlaceholder, StringComparison.OrdinalIgnoreCase) >= 0

    let restoreRedactedAuthorizationHeader
        (existingHeaders: (string * string) list)
        (incomingHeaders: (string * string) list)
        : (string * string) list =
        let existingAuthValue =
            existingHeaders
            |> List.tryFind (fun (key, _) -> isAuthorizationHeaderKey key)
            |> Option.map snd

        incomingHeaders
        |> List.choose (fun (key, value) ->
            if isAuthorizationHeaderKey key && isRedactedAuthorizationValue value then
                match existingAuthValue with
                | Some existingValue -> Some(key, existingValue)
                | None -> None
            else
                Some(key, value))