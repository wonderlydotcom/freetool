namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type RunId =
    | RunId of Guid

    static member NewId() = RunId(Guid.NewGuid())

    static member FromGuid(id: Guid) = RunId(id)

    member this.Value =
        let (RunId id) = this
        id

    override this.ToString() = this.Value.ToString()