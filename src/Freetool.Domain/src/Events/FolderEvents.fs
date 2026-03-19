namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

type FolderChange =
    | NameChanged of oldValue: FolderName * newValue: FolderName
    | ParentChanged of oldParentId: FolderId option * newParentId: FolderId option

type FolderCreatedEvent = {
    FolderId: FolderId
    Name: FolderName
    ParentId: FolderId option
    SpaceId: SpaceId
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type FolderUpdatedEvent = {
    FolderId: FolderId
    Changes: FolderChange list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type FolderDeletedEvent = {
    FolderId: FolderId
    Name: FolderName
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type FolderRestoredEvent = {
    FolderId: FolderId
    Name: FolderName
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

module FolderEvents =
    let folderCreated
        (actorUserId: UserId)
        (folderId: FolderId)
        (name: FolderName)
        (parentId: FolderId option)
        (spaceId: SpaceId)
        =
        {
            FolderId = folderId
            Name = name
            ParentId = parentId
            SpaceId = spaceId
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : FolderCreatedEvent

    let folderUpdated (actorUserId: UserId) (folderId: FolderId) (changes: FolderChange list) =
        {
            FolderId = folderId
            Changes = changes
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : FolderUpdatedEvent

    let folderDeleted (actorUserId: UserId) (folderId: FolderId) (name: FolderName) =
        {
            FolderId = folderId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : FolderDeletedEvent

    let folderRestored (actorUserId: UserId) (folderId: FolderId) (name: FolderName) =
        {
            FolderId = folderId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : FolderRestoredEvent