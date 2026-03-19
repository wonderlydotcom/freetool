namespace Freetool.Domain.ValueObjects

open System.Text.RegularExpressions
open Freetool.Domain

[<Struct>]
type Email =
    private
    | Email of string

    static member Create(email: string option) : Result<Email, DomainError> =
        match email with
        | None
        | Some "" -> Error(ValidationError "Email cannot be empty")
        | Some emailValue when emailValue.Length > 254 -> Error(ValidationError "Email cannot exceed 254 characters")
        | Some emailValue when not (Email.isValidFormat emailValue) -> Error(ValidationError "Invalid email format")
        | Some emailValue -> Ok(Email emailValue)

    member this.Value =
        let (Email email) = this
        email

    override this.ToString() = this.Value

    static member private isValidFormat(email: string) =
        let pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"
        Regex.IsMatch(email, pattern)