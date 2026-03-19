namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type InputTitle =
    | InputTitle of string

    static member Create(title: string option) : Result<InputTitle, DomainError> =
        match title with
        | None
        | Some "" -> Error(ValidationError "Input title cannot be empty")
        | Some titleValue -> Ok(InputTitle(titleValue.Trim()))

    member this.Value =
        let (InputTitle title) = this
        title

    override this.ToString() = this.Value