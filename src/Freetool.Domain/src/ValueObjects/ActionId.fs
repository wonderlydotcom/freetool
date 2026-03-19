namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type ActionId =
    | ActionId of string

    static member Create(actionId: string option) : Result<ActionId, DomainError> =
        match actionId |> Option.map (fun value -> value.Trim()) with
        | None
        | Some "" -> Error(ValidationError "Action ID cannot be empty")
        | Some value when value.Length > 100 -> Error(ValidationError "Action ID cannot exceed 100 characters")
        | Some value -> Ok(ActionId value)

    member this.Value =
        let (ActionId value) = this
        value

    override this.ToString() = this.Value