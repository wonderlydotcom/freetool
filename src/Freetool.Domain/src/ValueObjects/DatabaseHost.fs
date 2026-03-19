namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabaseHost =
    private
    | DatabaseHost of string

    static member Create(host: string option) : Result<DatabaseHost, DomainError> =
        match host with
        | None
        | Some "" -> Error(ValidationError "Database host cannot be empty")
        | Some value when value.Length > 255 -> Error(ValidationError "Database host cannot exceed 255 characters")
        | Some value -> Ok(DatabaseHost(value.Trim()))

    member this.Value =
        let (DatabaseHost value) = this
        value

    override this.ToString() = this.Value