namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type RunStatus =
    | Pending
    | Running
    | Success
    | Failure
    | InvalidConfiguration

    static member Create(status: string) : Result<RunStatus, DomainError> =
        match status.ToLowerInvariant() with
        | "pending" -> Ok Pending
        | "running" -> Ok Running
        | "success" -> Ok Success
        | "failure" -> Ok Failure
        | "invalid_configuration" -> Ok InvalidConfiguration
        | _ -> Error(ValidationError $"Invalid run status: {status}")

    override this.ToString() =
        match this with
        | Pending -> "Pending"
        | Running -> "Running"
        | Success -> "Success"
        | Failure -> "Failure"
        | InvalidConfiguration -> "InvalidConfiguration"