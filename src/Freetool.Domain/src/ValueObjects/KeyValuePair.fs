namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type KeyValuePair =
    private
    | KeyValuePair of key: string * value: string

    static member Create(key: string option, value: string option) : Result<KeyValuePair, DomainError> =
        match key with
        | None
        | Some "" -> Error(ValidationError "Key cannot be empty")
        | Some keyValue when keyValue.Length > 100 -> Error(ValidationError "Key cannot exceed 100 characters")
        | Some keyValue ->
            match value with
            | None -> Error(ValidationError "Value cannot be null")
            | Some "" -> Error(ValidationError "Value cannot be empty")
            | Some v when v.Length > 1_000 -> Error(ValidationError "Value cannot exceed 1_000 characters")
            | Some v -> Ok(KeyValuePair(keyValue.Trim(), v))

    static member Create(key: string, value: string) : Result<KeyValuePair, DomainError> =
        KeyValuePair.Create(Some key, Some value)

    member this.Key =
        let (KeyValuePair(key, _)) = this
        key

    member this.Value =
        let (KeyValuePair(_, value)) = this
        value

    override this.ToString() = $"{this.Key}={this.Value}"