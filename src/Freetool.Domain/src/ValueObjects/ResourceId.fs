namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type ResourceId =
    | ResourceId of Guid

    static member NewId() = ResourceId(Guid.NewGuid())

    static member FromGuid(id: Guid) = ResourceId(id)

    member this.Value =
        let (ResourceId id) = this
        id

    override this.ToString() = this.Value.ToString()