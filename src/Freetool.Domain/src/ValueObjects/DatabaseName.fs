namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabaseName =
    private
    | DatabaseName of string

    static member Create(name: string option) : Result<DatabaseName, DomainError> =
        match name with
        | None
        | Some "" -> Error(ValidationError "Database name cannot be empty")
        | Some value when value.Length > 200 -> Error(ValidationError "Database name cannot exceed 200 characters")
        | Some value -> Ok(DatabaseName(value.Trim()))

    member this.Value =
        let (DatabaseName value) = this
        value

    override this.ToString() = this.Value