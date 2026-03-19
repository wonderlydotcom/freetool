namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabaseEngine =
    | Postgres

    static member Create(engine: string) : Result<DatabaseEngine, DomainError> =
        let normalized = engine.Trim().ToUpperInvariant().Replace("-", "_")

        match normalized with
        | "POSTGRES"
        | "POSTGRESQL"
        | "PG" -> Ok Postgres
        | _ -> Error(ValidationError $"Invalid database engine: {engine}")

    override this.ToString() =
        match this with
        | Postgres -> "POSTGRES"