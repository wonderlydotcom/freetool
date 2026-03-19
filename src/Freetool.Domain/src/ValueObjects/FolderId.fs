namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type FolderId =
    | FolderId of Guid

    static member NewId() = FolderId(Guid.NewGuid())

    static member FromGuid(id: Guid) = FolderId(id)

    member this.Value =
        let (FolderId id) = this
        id

    override this.ToString() = this.Value.ToString()