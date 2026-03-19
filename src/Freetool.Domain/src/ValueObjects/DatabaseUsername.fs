namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabaseUsername =
    private
    | DatabaseUsername of string

    static member Create(username: string option) : Result<DatabaseUsername, DomainError> =
        match username with
        | None
        | Some "" -> Error(ValidationError "Database username cannot be empty")
        | Some value when value.Length > 128 -> Error(ValidationError "Database username cannot exceed 128 characters")
        | Some value -> Ok(DatabaseUsername(value.Trim()))

    member this.Value =
        let (DatabaseUsername value) = this
        value

    override this.ToString() = this.Value