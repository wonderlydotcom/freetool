namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabaseAuthScheme =
    | UsernamePassword

    static member Create(scheme: string) : Result<DatabaseAuthScheme, DomainError> =
        let normalized = scheme.Trim().ToUpperInvariant().Replace("-", "_")

        match normalized with
        | "USERNAME_PASSWORD"
        | "USERPASSWORD"
        | "USER_PASS" -> Ok UsernamePassword
        | _ -> Error(ValidationError $"Invalid database authentication scheme: {scheme}")

    override this.ToString() =
        match this with
        | UsernamePassword -> "USERNAME_PASSWORD"