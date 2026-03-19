namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

type UserChange =
    | NameChanged of oldValue: string * newValue: string
    | EmailChanged of oldValue: Email * newValue: Email
    | ProfilePicChanged of oldValue: Url option * newValue: Url option
    | Activated of name: string

type UserCreatedEvent = {
    UserId: UserId
    Name: string
    Email: Email
    ProfilePicUrl: Url option
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type UserUpdatedEvent = {
    UserId: UserId
    Changes: UserChange list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type UserDeletedEvent = {
    UserId: UserId
    Name: string // For audit log display (entity is deleted, can't look up)
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type UserInvitedEvent = {
    UserId: UserId
    Email: Email
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type UserActivatedEvent = {
    UserId: UserId
    Name: string
    OccurredAt: DateTime
    EventId: Guid
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.UserId

module UserEvents =
    let userCreated (actorUserId: UserId) (userId: UserId) (name: string) (email: Email) (profilePicUrl: Url option) =
        {
            UserId = userId
            Name = name
            Email = email
            ProfilePicUrl = profilePicUrl
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : UserCreatedEvent

    let userUpdated (actorUserId: UserId) (userId: UserId) (changes: UserChange list) =
        {
            UserId = userId
            Changes = changes
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : UserUpdatedEvent

    let userDeleted (actorUserId: UserId) (userId: UserId) (name: string) =
        {
            UserId = userId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : UserDeletedEvent

    let userInvited (actorUserId: UserId) (userId: UserId) (email: Email) =
        {
            UserId = userId
            Email = email
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : UserInvitedEvent

    let userActivated (userId: UserId) (name: string) =
        {
            UserId = userId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
        }
        : UserActivatedEvent