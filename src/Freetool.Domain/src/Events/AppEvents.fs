namespace Freetool.Domain.Events

open System
open Freetool.Domain
open Freetool.Domain.ValueObjects

// CLIMutable for EntityFramework
[<CLIMutable>]
type Input = {
    Title: string
    Description: string option
    Type: InputType
    Required: bool
    DefaultValue: DefaultValue option
}

type AppChange =
    | NameChanged of oldValue: AppName * newValue: AppName
    | InputsChanged of oldInputs: Input list * newInputs: Input list
    | FolderChanged of oldFolderId: FolderId option * newFolderId: FolderId option
    | UrlPathChanged of oldValue: string option * newValue: string option
    | UrlParametersChanged of oldUrlParams: KeyValuePair list * newUrlParams: KeyValuePair list
    | HeadersChanged of oldHeaders: KeyValuePair list * newHeaders: KeyValuePair list
    | BodyChanged of oldBody: KeyValuePair list * newBody: KeyValuePair list
    | HttpMethodChanged of oldValue: HttpMethod * newValue: HttpMethod
    | UseDynamicJsonBodyChanged of oldValue: bool * newValue: bool
    | SqlConfigChanged of
        oldValue: Freetool.Domain.Entities.SqlQueryConfig option *
        newValue: Freetool.Domain.Entities.SqlQueryConfig option
    | DescriptionChanged of oldValue: string option * newValue: string option

type AppCreatedEvent = {
    AppId: AppId
    Name: AppName
    FolderId: FolderId option
    ResourceId: ResourceId
    HttpMethod: HttpMethod
    Inputs: Input list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type AppUpdatedEvent = {
    AppId: AppId
    Changes: AppChange list
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type AppDeletedEvent = {
    AppId: AppId
    Name: AppName
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

type AppRestoredEvent = {
    AppId: AppId
    Name: AppName
    OccurredAt: DateTime
    EventId: Guid
    ActorUserId: UserId
} with

    interface IDomainEvent with
        member this.OccurredAt = this.OccurredAt
        member this.EventId = this.EventId
        member this.UserId = this.ActorUserId

module AppEvents =
    let appCreated
        (actorUserId: UserId)
        (appId: AppId)
        (name: AppName)
        (folderId: FolderId option)
        (resourceId: ResourceId)
        (httpMethod: HttpMethod)
        (inputs: Input list)
        =
        {
            AppId = appId
            Name = name
            FolderId = folderId
            ResourceId = resourceId
            HttpMethod = httpMethod
            Inputs = inputs
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : AppCreatedEvent

    let appUpdated (actorUserId: UserId) (appId: AppId) (changes: AppChange list) =
        {
            AppId = appId
            Changes = changes
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : AppUpdatedEvent

    let appDeleted (actorUserId: UserId) (appId: AppId) (name: AppName) =
        {
            AppId = appId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : AppDeletedEvent

    let appRestored (actorUserId: UserId) (appId: AppId) (name: AppName) =
        {
            AppId = appId
            Name = name
            OccurredAt = DateTime.UtcNow
            EventId = Guid.NewGuid()
            ActorUserId = actorUserId
        }
        : AppRestoredEvent