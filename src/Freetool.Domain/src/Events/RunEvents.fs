namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

type RunCreatedEvent = {
    RunId: RunId
    AppId: AppId
    InputValues: RunInputValue list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type RunStatusChangedEvent = {
    RunId: RunId
    OldStatus: RunStatus
    NewStatus: RunStatus
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

module RunEvents =
    let runCreated (actorUserId: UserId) (runId: RunId) (appId: AppId) (inputValues: RunInputValue list) =
        {
            RunId = runId
            AppId = appId
            InputValues = inputValues
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : RunCreatedEvent

    let runStatusChanged (actorUserId: UserId) (runId: RunId) (oldStatus: RunStatus) (newStatus: RunStatus) =
        {
            RunId = runId
            OldStatus = oldStatus
            NewStatus = newStatus
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : RunStatusChangedEvent