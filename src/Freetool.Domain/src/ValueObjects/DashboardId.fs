namespace Freetool.Domain.ValueObjects

open System

[<Struct>]
type DashboardId =
    | DashboardId of Guid

    static member NewId() = DashboardId(Guid.NewGuid())

    static member FromGuid(id: Guid) = DashboardId(id)

    member this.Value =
        let (DashboardId id) = this
        id

    override this.ToString() = this.Value.ToString()