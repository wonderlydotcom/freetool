namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type ResourceDescription =
    private
    | ResourceDescription of string

    static member Create(description: string option) : Result<ResourceDescription, DomainError> =
        match description with
        | None
        | Some "" -> Error(ValidationError "Resource description cannot be empty")
        | Some descValue when descValue.Length > 500 ->
            Error(ValidationError "Resource description cannot exceed 500 characters")
        | Some descValue -> Ok(ResourceDescription(descValue.Trim()))

    member this.Value =
        let (ResourceDescription description) = this
        description

    override this.ToString() = this.Value