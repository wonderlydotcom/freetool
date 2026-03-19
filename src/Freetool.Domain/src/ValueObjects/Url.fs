namespace Freetool.Domain.ValueObjects

open System.Text.RegularExpressions
open Freetool.Domain

[<Struct>]
type Url =
    private
    | Url of string

    static member Create(url: string option) : Result<Url, DomainError> =
        match url with
        | None
        | Some "" -> Error(ValidationError "URL cannot be empty")
        | Some urlValue when urlValue.Length > 2_000 -> Error(ValidationError "URL cannot exceed 2000 characters")
        | Some urlValue when not (Url.isValidFormat urlValue) -> Error(ValidationError "Invalid URL format")
        | Some urlValue -> Ok(Url urlValue)

    member this.Value =
        let (Url url) = this
        url

    override this.ToString() = this.Value

    static member private isValidFormat(url: string) =
        let pattern =
            @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)"

        Regex.IsMatch(url, pattern)