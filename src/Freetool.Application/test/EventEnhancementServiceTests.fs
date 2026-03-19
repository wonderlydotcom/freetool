module Freetool.Application.Tests.EventEnhancementServiceTests

open System
open System.Threading.Tasks
open System.Text.Json
open System.Text.Json.Serialization
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Application.Interfaces
open Freetool.Application.Services

// Shared JSON options for serializing events (matches EventRepository)
let jsonOptions =
    let options = JsonSerializerOptions()
    options.Converters.Add(JsonFSharpConverter())
    options

// Mock implementations for repositories
type MockUserRepository() =
    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) = task { return None }
        member _.GetByEmailAsync(email: Email) = task { return None }
        member _.GetAllAsync skip take = task { return [] }
        member _.AddAsync(user: ValidatedUser) = task { return Ok() }
        member _.UpdateAsync(user: ValidatedUser) = task { return Ok() }
        member _.DeleteAsync(user: ValidatedUser) = task { return Ok() }
        member _.ExistsAsync(userId: UserId) = task { return false }
        member _.ExistsByEmailAsync(email: Email) = task { return false }
        member _.GetCountAsync() = task { return 0 }

type MockAppRepository() =
    interface IAppRepository with
        member _.GetByIdAsync(appId: AppId) = task { return None }
        member _.GetByNameAndFolderIdAsync name folderId = task { return None }
        member _.GetByFolderIdAsync folderId skip take = task { return [] }
        member _.GetAllAsync skip take = task { return [] }
        member _.GetBySpaceIdsAsync spaceIds skip take = task { return [] }
        member _.AddAsync(app: ValidatedApp) = task { return Ok() }
        member _.UpdateAsync(app: ValidatedApp) = task { return Ok() }
        member _.DeleteAsync appId userId = task { return Ok() }
        member _.ExistsAsync(appId: AppId) = task { return false }
        member _.ExistsByNameAndFolderIdAsync name folderId = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountByFolderIdAsync folderId = task { return 0 }
        member _.GetCountBySpaceIdsAsync spaceIds = task { return 0 }
        member _.GetByResourceIdAsync resourceId = task { return [] }
        member _.GetDeletedByFolderIdsAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

type MockDashboardRepository() =
    interface IDashboardRepository with
        member _.GetByIdAsync(_dashboardId: DashboardId) = task { return None }
        member _.GetByFolderIdAsync _folderId _skip _take = task { return [] }
        member _.GetBySpaceIdsAsync _spaceIds _skip _take = task { return [] }
        member _.GetAllAsync _skip _take = task { return [] }
        member _.AddAsync(_dashboard) = task { return Ok() }
        member _.UpdateAsync(_dashboard) = task { return Ok() }
        member _.DeleteAsync _ _ = task { return Ok() }
        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetCountAsync() = task { return 0 }

type MockFolderRepository() =
    interface IFolderRepository with
        member _.GetByIdAsync(folderId: FolderId) = task { return None }
        member _.GetChildrenAsync(folderId: FolderId) = task { return [] }
        member _.GetRootFoldersAsync skip take = task { return [] }
        member _.GetAllAsync skip take = task { return [] }
        member _.GetBySpaceAsync spaceId skip take = task { return [] }
        member _.GetBySpaceIdsAsync spaceIds skip take = task { return [] }
        member _.AddAsync(folder: ValidatedFolder) = task { return Ok() }
        member _.UpdateAsync(folder: ValidatedFolder) = task { return Ok() }
        member _.DeleteAsync(folder: ValidatedFolder) = task { return Ok() }
        member _.ExistsAsync(folderId: FolderId) = task { return false }
        member _.ExistsByNameInParentAsync name parentId = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountBySpaceAsync spaceId = task { return 0 }
        member _.GetCountBySpaceIdsAsync spaceIds = task { return 0 }
        member _.GetRootCountAsync() = task { return 0 }
        member _.GetChildCountAsync parentId = task { return 0 }
        member _.GetDeletedBySpaceAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreWithChildrenAsync _ = task { return Ok 0 }
        member _.CheckNameConflictAsync _ _ _ = task { return false }

type MockSpaceRepository() =
    interface ISpaceRepository with
        member _.GetByIdAsync(spaceId: SpaceId) = task { return None }
        member _.GetByNameAsync(name: string) = task { return None }
        member _.GetAllAsync skip take = task { return [] }
        member _.GetByUserIdAsync userId = task { return [] }
        member _.GetByModeratorUserIdAsync userId = task { return [] }
        member _.AddAsync(space: ValidatedSpace) = task { return Ok() }
        member _.UpdateAsync(space: ValidatedSpace) = task { return Ok() }
        member _.DeleteAsync(space: ValidatedSpace) = task { return Ok() }
        member _.ExistsAsync(spaceId: SpaceId) = task { return false }
        member _.ExistsByNameAsync(name: string) = task { return false }
        member _.GetCountAsync() = task { return 0 }

type MockResourceRepository() =
    interface IResourceRepository with
        member _.GetByIdAsync(resourceId: ResourceId) = task { return None }
        member _.GetAllAsync skip take = task { return [] }
        member _.GetBySpaceAsync spaceId skip take = task { return [] }
        member _.GetCountBySpaceAsync spaceId = task { return 0 }
        member _.AddAsync(resource: ValidatedResource) = task { return Ok() }
        member _.UpdateAsync(resource: ValidatedResource) = task { return Ok() }
        member _.DeleteAsync(resource: ValidatedResource) = task { return Ok() }
        member _.ExistsAsync(resourceId: ResourceId) = task { return false }
        member _.ExistsByNameAsync(name: ResourceName) = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetDeletedBySpaceAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

let createService () =
    EventEnhancementService(
        MockUserRepository(),
        MockAppRepository(),
        MockDashboardRepository(),
        MockFolderRepository(),
        MockResourceRepository(),
        MockSpaceRepository()
    )
    :> IEventEnhancementService

// Helper to create EventData from a domain event
let createEventData (event: 'T :> IDomainEvent) (eventType: EventType) (entityType: EntityType) (entityId: string) =
    let eventDataStr = JsonSerializer.Serialize(event, jsonOptions)

    {
        Id = Guid.NewGuid()
        EventId = (event :> IDomainEvent).EventId.ToString()
        EventType = eventType
        EntityType = entityType
        EntityId = entityId
        EventData = eventDataStr
        OccurredAt = (event :> IDomainEvent).OccurredAt
        CreatedAt = DateTime.UtcNow
        UserId = (event :> IDomainEvent).UserId
    }

[<Fact>]
let ``FolderCreatedEvent extracts name correctly`` () = task {
    let service = createService ()
    let folderId = FolderId.NewId()

    let folderName =
        FolderName.Create(Some "My Test Folder") |> Result.toOption |> Option.get

    let actorUserId = UserId.NewId()

    let spaceId = SpaceId.NewId()
    let event = FolderEvents.folderCreated actorUserId folderId folderName None spaceId

    let eventData =
        createEventData event (FolderEvents FolderCreatedEvent) EntityType.Folder (folderId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Equal("My Test Folder", enhanced.EntityName)
    Assert.Contains("created folder", enhanced.EventSummary)
}

[<Fact>]
let ``FolderDeletedEvent extracts name from event`` () = task {
    let service = createService ()
    let folderId = FolderId.NewId()

    let folderName =
        FolderName.Create(Some "Deleted Folder") |> Result.toOption |> Option.get

    let actorUserId = UserId.NewId()

    let event = FolderEvents.folderDeleted actorUserId folderId folderName

    let eventData =
        createEventData event (FolderEvents FolderDeletedEvent) EntityType.Folder (folderId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Equal("Deleted Folder", enhanced.EntityName)
    Assert.Contains("deleted folder", enhanced.EventSummary)
}

[<Fact>]
let ``ResourceDeletedEvent extracts name from event`` () = task {
    let service = createService ()
    let resourceId = ResourceId.NewId()

    let resourceName =
        ResourceName.Create(Some "My Resource") |> Result.toOption |> Option.get

    let actorUserId = UserId.NewId()

    let event = ResourceEvents.resourceDeleted actorUserId resourceId resourceName

    let eventData =
        createEventData event (ResourceEvents ResourceDeletedEvent) EntityType.Resource (resourceId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Equal("My Resource", enhanced.EntityName)
    Assert.Contains("deleted resource", enhanced.EventSummary)
}

[<Fact>]
let ``AppDeletedEvent extracts name from event`` () = task {
    let service = createService ()
    let appId = AppId.NewId()
    let appName = AppName.Create(Some "My App") |> Result.toOption |> Option.get
    let actorUserId = UserId.NewId()

    let event = AppEvents.appDeleted actorUserId appId appName

    let eventData =
        createEventData event (AppEvents AppDeletedEvent) EntityType.App (appId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Equal("My App", enhanced.EntityName)
    Assert.Contains("deleted app", enhanced.EventSummary)
}

[<Fact>]
let ``SpaceDefaultMemberPermissionsChangedEvent enhances with correct summary`` () = task {
    let service = createService ()
    let actorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    let event =
        SpaceEvents.spaceDefaultMemberPermissionsChanged actorUserId spaceId "Engineering" [ "CreateApp"; "RunApp" ] [
            "DeleteApp"
        ]

    let eventData =
        createEventData
            event
            (SpaceEvents SpaceDefaultMemberPermissionsChangedEvent)
            EntityType.Space
            (spaceId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Equal("Engineering", enhanced.EntityName)
    Assert.Contains("changed default member permissions in space", enhanced.EventSummary)
}

[<Fact>]
let ``DashboardPreparedEvent enhances with runtime summary`` () = task {
    let service = createService ()
    let actorUserId = UserId.NewId()
    let dashboardId = DashboardId.NewId()
    let prepareAppId = AppId.NewId()
    let prepareRunId = RunId.NewId()

    let event =
        DashboardEvents.dashboardPrepared actorUserId dashboardId prepareAppId prepareRunId

    let eventData =
        createEventData
            event
            (DashboardEvents DashboardPreparedEvent)
            EntityType.Dashboard
            (dashboardId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Contains("Dashboard", enhanced.EntityName)
    Assert.Contains("prepared dashboard", enhanced.EventSummary)
}

[<Fact>]
let ``DashboardActionFailedEvent enhances with runtime failure summary`` () = task {
    let service = createService ()
    let actorUserId = UserId.NewId()
    let dashboardId = DashboardId.NewId()
    let actionId = ActionId.Create(Some "approve") |> Result.toOption |> Option.get
    let actionAppId = AppId.NewId()

    let event =
        DashboardEvents.dashboardActionFailed actorUserId dashboardId actionId (Some actionAppId) "service unavailable"

    let eventData =
        createEventData
            event
            (DashboardEvents DashboardActionFailedEvent)
            EntityType.Dashboard
            (dashboardId.Value.ToString())

    let! enhanced = service.EnhanceEventAsync(eventData)

    Assert.Contains("Dashboard", enhanced.EntityName)
    Assert.Contains("failed a dashboard action", enhanced.EventSummary)
}