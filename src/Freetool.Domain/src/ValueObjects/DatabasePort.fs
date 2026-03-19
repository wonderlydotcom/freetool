namespace Freetool.Domain.ValueObjects

open Freetool.Domain

[<Struct>]
type DatabasePort =
    private
    | DatabasePort of int

    static member Create(port: int option) : Result<DatabasePort, DomainError> =
        match port with
        | None -> Error(ValidationError "Database port is required")
        | Some value when value < 1 || value > 65535 ->
            Error(ValidationError "Database port must be between 1 and 65535")
        | Some value -> Ok(DatabasePort(value))

    member this.Value =
        let (DatabasePort value) = this
        value

    override this.ToString() = this.Value.ToString()