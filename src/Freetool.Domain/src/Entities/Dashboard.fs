namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

[<Table("Dashboards")>]
[<Index([| "Name"; "FolderId" |], IsUnique = true, Name = "IX_Dashboards_Name_FolderId")>]
[<CLIMutable>]
type DashboardData = {
    [<Key>]
    Id: DashboardId

    [<Required>]
    [<MaxLength(100)>]
    Name: DashboardName

    [<Required>]
    FolderId: FolderId

    PrepareAppId: AppId option

    [<Required>]
    [<Column(TypeName = "TEXT")>]
    Configuration: string

    [<Required>]
    CreatedAt: DateTime

    [<Required>]
    UpdatedAt: DateTime

    [<JsonIgnore>]
    IsDeleted: bool
}

type Dashboard = EventSourcingAggregate<DashboardData>

type UnvalidatedDashboard = Dashboard
type ValidatedDashboard = Dashboard

module Dashboard =
    let private normalizeConfiguration (configuration: string) : string =
        if String.IsNullOrWhiteSpace(configuration) then
            "{}"
        else
            configuration.Trim()

    let fromData (dashboardData: DashboardData) : ValidatedDashboard = {
        State = dashboardData
        UncommittedEvents = []
    }

    let create
        (actorUserId: UserId)
        (name: string)
        (folderId: FolderId)
        (prepareAppId: AppId option)
        (configuration: string)
        : Result<ValidatedDashboard, DomainError> =
        match DashboardName.Create(Some name) with
        | Error err -> Error err
        | Ok validName ->
            let normalizedConfiguration = normalizeConfiguration configuration

            let dashboardData = {
                Id = DashboardId.NewId()
                Name = validName
                FolderId = folderId
                PrepareAppId = prepareAppId
                Configuration = normalizedConfiguration
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                IsDeleted = false
            }

            let dashboardCreatedEvent =
                DashboardEvents.dashboardCreated
                    actorUserId
                    dashboardData.Id
                    validName
                    folderId
                    prepareAppId
                    normalizedConfiguration

            Ok {
                State = dashboardData
                UncommittedEvents = [ dashboardCreatedEvent :> IDomainEvent ]
            }

    let updateName
        (actorUserId: UserId)
        (newName: string)
        (dashboard: ValidatedDashboard)
        : Result<ValidatedDashboard, DomainError> =
        match DashboardName.Create(Some newName) with
        | Error err -> Error err
        | Ok validName ->
            let oldName = dashboard.State.Name

            let updatedDashboardData = {
                dashboard.State with
                    Name = validName
                    UpdatedAt = DateTime.UtcNow
            }

            let event =
                DashboardEvents.dashboardUpdated actorUserId dashboard.State.Id [
                    DashboardChange.NameChanged(oldName, validName)
                ]

            Ok {
                State = updatedDashboardData
                UncommittedEvents = dashboard.UncommittedEvents @ [ event :> IDomainEvent ]
            }

    let updateConfiguration
        (actorUserId: UserId)
        (configuration: string)
        (dashboard: ValidatedDashboard)
        : ValidatedDashboard =
        let normalizedConfiguration = normalizeConfiguration configuration
        let oldConfiguration = dashboard.State.Configuration

        let updatedDashboardData = {
            dashboard.State with
                Configuration = normalizedConfiguration
                UpdatedAt = DateTime.UtcNow
        }

        let event =
            DashboardEvents.dashboardUpdated actorUserId dashboard.State.Id [
                DashboardChange.ConfigurationChanged(oldConfiguration, normalizedConfiguration)
            ]

        {
            State = updatedDashboardData
            UncommittedEvents = dashboard.UncommittedEvents @ [ event :> IDomainEvent ]
        }

    let updatePrepareApp
        (actorUserId: UserId)
        (prepareAppId: AppId option)
        (dashboard: ValidatedDashboard)
        : ValidatedDashboard =
        let oldValue = dashboard.State.PrepareAppId

        let updatedDashboardData = {
            dashboard.State with
                PrepareAppId = prepareAppId
                UpdatedAt = DateTime.UtcNow
        }

        let event =
            DashboardEvents.dashboardUpdated actorUserId dashboard.State.Id [
                DashboardChange.PrepareAppChanged(oldValue, prepareAppId)
            ]

        {
            State = updatedDashboardData
            UncommittedEvents = dashboard.UncommittedEvents @ [ event :> IDomainEvent ]
        }

    let markForDeletion (actorUserId: UserId) (dashboard: ValidatedDashboard) : ValidatedDashboard =
        let event =
            DashboardEvents.dashboardDeleted actorUserId dashboard.State.Id dashboard.State.Name

        {
            dashboard with
                UncommittedEvents = dashboard.UncommittedEvents @ [ event :> IDomainEvent ]
        }

    let getUncommittedEvents (dashboard: ValidatedDashboard) : IDomainEvent list = dashboard.UncommittedEvents

    let getId (dashboard: Dashboard) : DashboardId = dashboard.State.Id

    let getName (dashboard: Dashboard) : string = dashboard.State.Name.Value

    let getFolderId (dashboard: Dashboard) : FolderId = dashboard.State.FolderId