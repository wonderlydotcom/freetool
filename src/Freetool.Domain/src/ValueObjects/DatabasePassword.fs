namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabasePassword =
    private
    | DatabasePassword of string

    static member Create(password: string option) : Result<DatabasePassword, DomainError> =
        match password with
        | None
        | Some "" -> Error(ValidationError "Database password cannot be empty")
        | Some value when value.Length > 256 -> Error(ValidationError "Database password cannot exceed 256 characters")
        | Some value -> Ok(DatabasePassword(value))

    member this.Value =
        let (DatabasePassword value) = this
        value

    override this.ToString() = "[REDACTED]"