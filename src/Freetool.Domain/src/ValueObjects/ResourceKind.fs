namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type ResourceKind =
    | Http
    | Sql

    static member Create(kind: string) : Result<ResourceKind, DomainError> =
        let normalized = kind.Trim().ToUpperInvariant()

        match normalized with
        | "HTTP" -> Ok Http
        | "SQL" -> Ok Sql
        | _ -> Error(ValidationError $"Invalid resource kind: {kind}")

    override this.ToString() =
        match this with
        | Http -> "HTTP"
        | Sql -> "SQL"