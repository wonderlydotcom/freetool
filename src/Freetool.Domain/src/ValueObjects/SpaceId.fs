namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type SpaceId =
    | SpaceId of Guid

    static member NewId() = SpaceId(Guid.NewGuid())

    static member FromGuid(id: Guid) = SpaceId(id)

    member this.Value =
        let (SpaceId id) = this
        id

    override this.ToString() = this.Value.ToString()