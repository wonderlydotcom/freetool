namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DashboardName =
    | DashboardName of string

    static member Create(name: string option) : Result<DashboardName, DomainError> =
        match name with
        | None
        | Some "" -> Error(ValidationError "Dashboard name cannot be empty")
        | Some nameValue when nameValue.Trim().Length = 0 -> Error(ValidationError "Dashboard name cannot be empty")
        | Some nameValue when nameValue.Length > 100 ->
            Error(ValidationError "Dashboard name cannot exceed 100 characters")
        | Some nameValue -> Ok(DashboardName(nameValue.Trim()))

    member this.Value =
        let (DashboardName name) = this
        name

    override this.ToString() = this.Value