namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

/// Represents changes that can occur to a Space
type SpaceChange =
    | NameChanged of oldValue: string * newValue: string
    | ModeratorChanged of oldModerator: UserId * newModerator: UserId
    | MemberAdded of userId: UserId
    | MemberRemoved of userId: UserId

/// Event raised when a new Space is created
type SpaceCreatedEvent = {
    SpaceId: SpaceId
    Name: string
    ModeratorUserId: UserId
    InitialMemberIds: UserId list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

/// Event raised when a Space is updated
type SpaceUpdatedEvent = {
    SpaceId: SpaceId
    Changes: SpaceChange list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

/// Event raised when a Space is deleted
/// Includes Name for audit log display (per CLAUDE.md checklist)
type SpaceDeletedEvent = {
    SpaceId: SpaceId
    Name: string
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

/// Event raised when a Space member's permissions are changed
/// Includes names for audit log display (entities may be deleted later)
type SpacePermissionsChangedEvent = {
    SpaceId: SpaceId
    SpaceName: string
    TargetUserId: UserId
    TargetUserName: string
    PermissionsGranted: string list
    PermissionsRevoked: string list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

/// Event raised when default permissions for all space members are changed
type SpaceDefaultMemberPermissionsChangedEvent = {
    SpaceId: SpaceId
    SpaceName: string
    PermissionsGranted: string list
    PermissionsRevoked: string list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

module SpaceEvents =
    /// Creates a SpaceCreatedEvent
    let spaceCreated
        (actorUserId: UserId)
        (spaceId: SpaceId)
        (name: string)
        (moderatorUserId: UserId)
        (initialMemberIds: UserId list)
        =
        {
            SpaceId = spaceId
            Name = name
            ModeratorUserId = moderatorUserId
            InitialMemberIds = initialMemberIds
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : SpaceCreatedEvent

    /// Creates a SpaceUpdatedEvent
    let spaceUpdated (actorUserId: UserId) (spaceId: SpaceId) (changes: SpaceChange list) =
        {
            SpaceId = spaceId
            Changes = changes
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : SpaceUpdatedEvent

    /// Creates a SpaceDeletedEvent with name for audit log display
    let spaceDeleted (actorUserId: UserId) (spaceId: SpaceId) (name: string) =
        {
            SpaceId = spaceId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : SpaceDeletedEvent

    /// Creates a SpacePermissionsChangedEvent
    let spacePermissionsChanged
        (actorUserId: UserId)
        (spaceId: SpaceId)
        (spaceName: string)
        (targetUserId: UserId)
        (targetUserName: string)
        (permissionsGranted: string list)
        (permissionsRevoked: string list)
        =
        {
            SpaceId = spaceId
            SpaceName = spaceName
            TargetUserId = targetUserId
            TargetUserName = targetUserName
            PermissionsGranted = permissionsGranted
            PermissionsRevoked = permissionsRevoked
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : SpacePermissionsChangedEvent

    /// Creates a SpaceDefaultMemberPermissionsChangedEvent
    let spaceDefaultMemberPermissionsChanged
        (actorUserId: UserId)
        (spaceId: SpaceId)
        (spaceName: string)
        (permissionsGranted: string list)
        (permissionsRevoked: string list)
        =
        {
            SpaceId = spaceId
            SpaceName = spaceName
            PermissionsGranted = permissionsGranted
            PermissionsRevoked = permissionsRevoked
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : SpaceDefaultMemberPermissionsChangedEvent