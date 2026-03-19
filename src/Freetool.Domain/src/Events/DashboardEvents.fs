namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

type DashboardChange =
    | NameChanged of oldValue: DashboardName * newValue: DashboardName
    | PrepareAppChanged of oldValue: AppId option * newValue: AppId option
    | ConfigurationChanged of oldValue: string * newValue: string

type DashboardCreatedEvent = {
    DashboardId: DashboardId
    Name: DashboardName
    FolderId: FolderId
    PrepareAppId: AppId option
    Configuration: string
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardUpdatedEvent = {
    DashboardId: DashboardId
    Changes: DashboardChange list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardDeletedEvent = {
    DashboardId: DashboardId
    Name: DashboardName
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardPreparedEvent = {
    DashboardId: DashboardId
    PrepareAppId: AppId
    PrepareRunId: RunId
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardPrepareFailedEvent = {
    DashboardId: DashboardId
    PrepareAppId: AppId option
    ErrorMessage: string
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardActionExecutedEvent = {
    DashboardId: DashboardId
    ActionId: ActionId
    ActionAppId: AppId
    ActionRunId: RunId
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type DashboardActionFailedEvent = {
    DashboardId: DashboardId
    ActionId: ActionId
    ActionAppId: AppId option
    ErrorMessage: string
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

module DashboardEvents =
    let dashboardCreated
        (actorUserId: UserId)
        (dashboardId: DashboardId)
        (name: DashboardName)
        (folderId: FolderId)
        (prepareAppId: AppId option)
        (configuration: string)
        =
        {
            DashboardId = dashboardId
            Name = name
            FolderId = folderId
            PrepareAppId = prepareAppId
            Configuration = configuration
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardCreatedEvent

    let dashboardUpdated (actorUserId: UserId) (dashboardId: DashboardId) (changes: DashboardChange list) =
        {
            DashboardId = dashboardId
            Changes = changes
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardUpdatedEvent

    let dashboardDeleted (actorUserId: UserId) (dashboardId: DashboardId) (name: DashboardName) =
        {
            DashboardId = dashboardId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardDeletedEvent

    let dashboardPrepared (actorUserId: UserId) (dashboardId: DashboardId) (prepareAppId: AppId) (prepareRunId: RunId) =
        {
            DashboardId = dashboardId
            PrepareAppId = prepareAppId
            PrepareRunId = prepareRunId
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardPreparedEvent

    let dashboardPrepareFailed
        (actorUserId: UserId)
        (dashboardId: DashboardId)
        (prepareAppId: AppId option)
        (errorMessage: string)
        =
        {
            DashboardId = dashboardId
            PrepareAppId = prepareAppId
            ErrorMessage = errorMessage
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardPrepareFailedEvent

    let dashboardActionExecuted
        (actorUserId: UserId)
        (dashboardId: DashboardId)
        (actionId: ActionId)
        (actionAppId: AppId)
        (actionRunId: RunId)
        =
        {
            DashboardId = dashboardId
            ActionId = actionId
            ActionAppId = actionAppId
            ActionRunId = actionRunId
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardActionExecutedEvent

    let dashboardActionFailed
        (actorUserId: UserId)
        (dashboardId: DashboardId)
        (actionId: ActionId)
        (actionAppId: AppId option)
        (errorMessage: string)
        =
        {
            DashboardId = dashboardId
            ActionId = actionId
            ActionAppId = actionAppId
            ErrorMessage = errorMessage
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : DashboardActionFailedEvent