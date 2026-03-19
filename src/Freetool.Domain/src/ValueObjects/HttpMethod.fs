namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type HttpMethod =
    | Delete
    | Get
    | Patch
    | Post
    | Put

    static member Create(method: string) : Result<HttpMethod, DomainError> =
        match method.ToUpperInvariant() with
        | "DELETE" -> Ok Delete
        | "GET" -> Ok Get
        | "PATCH" -> Ok Patch
        | "POST" -> Ok Post
        | "PUT" -> Ok Put
        | _ -> Error(ValidationError $"Invalid HTTP method: {method}")

    override this.ToString() =
        match this with
        | Delete -> "DELETE"
        | Get -> "GET"
        | Patch -> "PATCH"
        | Post -> "POST"
        | Put -> "PUT"