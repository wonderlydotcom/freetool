namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open Freetool.Domain.ValueObjects
open Microsoft.EntityFrameworkCore

type UserEvents =
    | UserCreatedEvent
    | UserUpdatedEvent
    | UserDeletedEvent
    | UserInvitedEvent
    | UserActivatedEvent

type AppEvents =
    | AppCreatedEvent
    | AppUpdatedEvent
    | AppDeletedEvent
    | AppRestoredEvent

type DashboardEvents =
    | DashboardCreatedEvent
    | DashboardUpdatedEvent
    | DashboardDeletedEvent
    | DashboardPreparedEvent
    | DashboardPrepareFailedEvent
    | DashboardActionExecutedEvent
    | DashboardActionFailedEvent

type FolderEvents =
    | FolderCreatedEvent
    | FolderUpdatedEvent
    | FolderDeletedEvent
    | FolderRestoredEvent

type ResourceEvents =
    | ResourceCreatedEvent
    | ResourceUpdatedEvent
    | ResourceDeletedEvent
    | ResourceRestoredEvent

type RunEvents =
    | RunCreatedEvent
    | RunStatusChangedEvent

type SpaceEvents =
    | SpaceCreatedEvent
    | SpaceUpdatedEvent
    | SpaceDeletedEvent
    | SpacePermissionsChangedEvent
    | SpaceDefaultMemberPermissionsChangedEvent

type EventType =
    | UserEvents of UserEvents
    | AppEvents of AppEvents
    | DashboardEvents of DashboardEvents
    | ResourceEvents of ResourceEvents
    | FolderEvents of FolderEvents
    | RunEvents of RunEvents
    | SpaceEvents of SpaceEvents

type EntityType =
    | User
    | App
    | Dashboard
    | Resource
    | Folder
    | Run
    | Space

module EventTypeConverter =
    let toString (eventType: EventType) : string =
        match eventType with
        | UserEvents userEvent ->
            match userEvent with
            | UserCreatedEvent -> "UserCreatedEvent"
            | UserUpdatedEvent -> "UserUpdatedEvent"
            | UserDeletedEvent -> "UserDeletedEvent"
            | UserInvitedEvent -> "UserInvitedEvent"
            | UserActivatedEvent -> "UserActivatedEvent"
        | AppEvents appEvent ->
            match appEvent with
            | AppCreatedEvent -> "AppCreatedEvent"
            | AppUpdatedEvent -> "AppUpdatedEvent"
            | AppDeletedEvent -> "AppDeletedEvent"
            | AppRestoredEvent -> "AppRestoredEvent"
        | DashboardEvents dashboardEvent ->
            match dashboardEvent with
            | DashboardCreatedEvent -> "DashboardCreatedEvent"
            | DashboardUpdatedEvent -> "DashboardUpdatedEvent"
            | DashboardDeletedEvent -> "DashboardDeletedEvent"
            | DashboardPreparedEvent -> "DashboardPreparedEvent"
            | DashboardPrepareFailedEvent -> "DashboardPrepareFailedEvent"
            | DashboardActionExecutedEvent -> "DashboardActionExecutedEvent"
            | DashboardActionFailedEvent -> "DashboardActionFailedEvent"
        | ResourceEvents resourceEvent ->
            match resourceEvent with
            | ResourceCreatedEvent -> "ResourceCreatedEvent"
            | ResourceUpdatedEvent -> "ResourceUpdatedEvent"
            | ResourceDeletedEvent -> "ResourceDeletedEvent"
            | ResourceRestoredEvent -> "ResourceRestoredEvent"
        | FolderEvents folderEvent ->
            match folderEvent with
            | FolderCreatedEvent -> "FolderCreatedEvent"
            | FolderUpdatedEvent -> "FolderUpdatedEvent"
            | FolderDeletedEvent -> "FolderDeletedEvent"
            | FolderRestoredEvent -> "FolderRestoredEvent"
        | RunEvents runEvent ->
            match runEvent with
            | RunCreatedEvent -> "RunCreatedEvent"
            | RunStatusChangedEvent -> "RunStatusChangedEvent"
        | SpaceEvents spaceEvent ->
            match spaceEvent with
            | SpaceCreatedEvent -> "SpaceCreatedEvent"
            | SpaceUpdatedEvent -> "SpaceUpdatedEvent"
            | SpaceDeletedEvent -> "SpaceDeletedEvent"
            | SpacePermissionsChangedEvent -> "SpacePermissionsChangedEvent"
            | SpaceDefaultMemberPermissionsChangedEvent -> "SpaceDefaultMemberPermissionsChangedEvent"

    let fromString (str: string) : EventType option =
        match str with
        | "UserCreatedEvent" -> Some(UserEvents UserCreatedEvent)
        | "UserUpdatedEvent" -> Some(UserEvents UserUpdatedEvent)
        | "UserDeletedEvent" -> Some(UserEvents UserDeletedEvent)
        | "UserInvitedEvent" -> Some(UserEvents UserInvitedEvent)
        | "UserActivatedEvent" -> Some(UserEvents UserActivatedEvent)
        | "AppCreatedEvent" -> Some(AppEvents AppCreatedEvent)
        | "AppUpdatedEvent" -> Some(AppEvents AppUpdatedEvent)
        | "AppDeletedEvent" -> Some(AppEvents AppDeletedEvent)
        | "AppRestoredEvent" -> Some(AppEvents AppRestoredEvent)
        | "DashboardCreatedEvent" -> Some(DashboardEvents DashboardCreatedEvent)
        | "DashboardUpdatedEvent" -> Some(DashboardEvents DashboardUpdatedEvent)
        | "DashboardDeletedEvent" -> Some(DashboardEvents DashboardDeletedEvent)
        | "DashboardPreparedEvent" -> Some(DashboardEvents DashboardPreparedEvent)
        | "DashboardPrepareFailedEvent" -> Some(DashboardEvents DashboardPrepareFailedEvent)
        | "DashboardActionExecutedEvent" -> Some(DashboardEvents DashboardActionExecutedEvent)
        | "DashboardActionFailedEvent" -> Some(DashboardEvents DashboardActionFailedEvent)
        | "ResourceCreatedEvent" -> Some(ResourceEvents ResourceCreatedEvent)
        | "ResourceUpdatedEvent" -> Some(ResourceEvents ResourceUpdatedEvent)
        | "ResourceDeletedEvent" -> Some(ResourceEvents ResourceDeletedEvent)
        | "ResourceRestoredEvent" -> Some(ResourceEvents ResourceRestoredEvent)
        | "FolderCreatedEvent" -> Some(FolderEvents FolderCreatedEvent)
        | "FolderUpdatedEvent" -> Some(FolderEvents FolderUpdatedEvent)
        | "FolderDeletedEvent" -> Some(FolderEvents FolderDeletedEvent)
        | "FolderRestoredEvent" -> Some(FolderEvents FolderRestoredEvent)
        | "RunCreatedEvent" -> Some(RunEvents RunCreatedEvent)
        | "RunStatusChangedEvent" -> Some(RunEvents RunStatusChangedEvent)
        | "SpaceCreatedEvent" -> Some(SpaceEvents SpaceCreatedEvent)
        | "SpaceUpdatedEvent" -> Some(SpaceEvents SpaceUpdatedEvent)
        | "SpaceDeletedEvent" -> Some(SpaceEvents SpaceDeletedEvent)
        | "SpacePermissionsChangedEvent" -> Some(SpaceEvents SpacePermissionsChangedEvent)
        | "SpaceDefaultMemberPermissionsChangedEvent" -> Some(SpaceEvents SpaceDefaultMemberPermissionsChangedEvent)
        | _ -> None

module EntityTypeConverter =
    let toString (entityType: EntityType) : string =
        match entityType with
        | User -> "User"
        | App -> "App"
        | Dashboard -> "Dashboard"
        | Resource -> "Resource"
        | Folder -> "Folder"
        | Run -> "Run"
        | Space -> "Space"

    let fromString (str: string) : EntityType option =
        match str with
        | "User" -> Some User
        | "App" -> Some App
        | "Dashboard" -> Some Dashboard
        | "Resource" -> Some Resource
        | "Folder" -> Some Folder
        | "Run" -> Some Run
        | "Space" -> Some Space
        | _ -> None

[<Table("Events")>]
[<Index([| "EventId" |], IsUnique = true, Name = "IX_Events_EventId")>]
[<Index([| "UserId" |], Name = "IX_Events_UserId")>]
[<Index([| "EventType" |], Name = "IX_Events_EventType")>]
[<Index([| "EntityType" |], Name = "IX_Events_EntityType")>]
[<Index([| "OccurredAt" |], Name = "IX_Events_OccurredAt")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type EventData = {
    [<Key>]
    [<Column("Id")>]
    Id: Guid

    [<Required>]
    [<Column("EventId")>]
    EventId: string

    [<Required>]
    [<Column("EventType")>]
    EventType: EventType

    [<Required>]
    [<Column("EntityType")>]
    EntityType: EntityType

    [<Required>]
    [<Column("EntityId")>]
    EntityId: string

    [<Required>]
    [<Column("EventData")>]
    EventData: string

    [<Required>]
    [<Column("OccurredAt")>]
    OccurredAt: DateTime

    [<Required>]
    [<Column("CreatedAt")>]
    CreatedAt: DateTime

    [<Required>]
    [<Column("UserId")>]
    UserId: UserId
}