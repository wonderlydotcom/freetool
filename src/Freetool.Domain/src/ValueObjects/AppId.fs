namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type AppId =
    | AppId of Guid

    static member NewId() = AppId(Guid.NewGuid())

    static member FromGuid(id: Guid) = AppId(id)

    member this.Value =
        let (AppId id) = this
        id

    override this.ToString() = this.Value.ToString()