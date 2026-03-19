namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type UserId =
    | UserId of Guid

    static member NewId() = UserId(Guid.NewGuid())

    static member FromGuid(id: Guid) = UserId(id)

    member this.Value =
        let (UserId id) = this
        id

    override this.ToString() = this.Value.ToString()