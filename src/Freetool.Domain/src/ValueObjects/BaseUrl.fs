namespace Freetool.Domain.ValueObjects

open System.Text.RegularExpressions
open Freetool.Domain

[<Struct>]
type BaseUrl =
    private
    | BaseUrl of string

    static member Create(url: string option) : Result<BaseUrl, DomainError> =
        match url with
        | None
        | Some "" -> Error(ValidationError "Base URL cannot be empty")
        | Some urlValue when urlValue.Length > 1_000 -> Error(ValidationError "Base URL cannot exceed 1_000 characters")
        | Some urlValue when not (BaseUrl.isValidFormat urlValue) -> Error(ValidationError "Invalid URL format")
        | Some urlValue -> Ok(BaseUrl(urlValue.Trim()))

    member this.Value =
        let (BaseUrl url) = this
        url

    override this.ToString() = this.Value

    static member private isValidFormat(url: string) =
        let pattern = @"^https?://[^\s/$.?#].[^\s]*$"
        Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)