namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type ResourceName =
    private
    | ResourceName of string

    static member Create(name: string option) : Result<ResourceName, DomainError> =
        match name with
        | None
        | Some "" -> Error(ValidationError "Resource name cannot be empty")
        | Some nameValue when nameValue.Length > 100 ->
            Error(ValidationError "Resource name cannot exceed 100 characters")
        | Some nameValue -> Ok(ResourceName(nameValue.Trim()))

    member this.Value =
        let (ResourceName name) = this
        name

    override this.ToString() = this.Value