namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type AppName =
    | AppName of string

    static member Create(name: string option) : Result<AppName, DomainError> =
        match name with
        | None
        | Some "" -> Error(ValidationError "App name cannot be empty")
        | Some nameValue when nameValue.Length > 100 -> Error(ValidationError "App name cannot exceed 100 characters")
        | Some nameValue -> Ok(AppName(nameValue.Trim()))

    member this.Value =
        let (AppName name) = this
        name

    override this.ToString() = this.Value