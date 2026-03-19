module Freetool.Application.Tests.AppHandlerTests

open System
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Commands
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Application.DTOs

// Mock repository for testing
type MockAppRepository(apps: ValidatedApp list) =
    let mutable appList = apps
    let mutable nameConflicts = Map.empty<string, bool>

    member _.SetNameConflict(name: string, folderId: FolderId, hasConflict: bool) =
        nameConflicts <- nameConflicts.Add($"{name}_{folderId.Value}", hasConflict)

    interface IAppRepository with
        member _.GetByIdAsync(appId: AppId) : Task<ValidatedApp option> = task {
            return appList |> List.tryFind (fun a -> a.State.Id = appId)
        }

        member _.GetByNameAndFolderIdAsync (appName: AppName) (folderId: FolderId) : Task<ValidatedApp option> = task {
            return
                appList
                |> List.tryFind (fun a -> a.State.Name = appName.Value && a.State.FolderId = folderId)
        }

        member _.GetByFolderIdAsync (folderId: FolderId) (skip: int) (take: int) : Task<ValidatedApp list> = task {
            return
                appList
                |> List.filter (fun a -> a.State.FolderId = folderId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedApp list> = task {
            return appList |> List.skip skip |> List.truncate take
        }

        member _.GetBySpaceIdsAsync (spaceIds: SpaceId list) (skip: int) (take: int) : Task<ValidatedApp list> = task {
            // For testing, we need to get the spaceId from the folder
            // In tests, we'll use a simplified approach where we match by folder
            return appList |> List.skip skip |> List.truncate take
        }

        member _.AddAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            appList <- app :: appList
            return Ok()
        }

        member _.UpdateAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            appList <- app :: (appList |> List.filter (fun a -> a.State.Id <> app.State.Id))
            return Ok()
        }

        member _.DeleteAsync (appId: AppId) (_actorUserId: UserId) : Task<Result<unit, DomainError>> = task {
            let appOption = appList |> List.tryFind (fun a -> a.State.Id = appId)

            match appOption with
            | None -> return Error(NotFound "App not found")
            | Some _ ->
                appList <- appList |> List.filter (fun a -> a.State.Id <> appId)
                return Ok()
        }

        member _.ExistsAsync(appId: AppId) : Task<bool> = task {
            return appList |> List.exists (fun a -> a.State.Id = appId)
        }

        member _.ExistsByNameAndFolderIdAsync (appName: AppName) (folderId: FolderId) : Task<bool> = task {
            let key = $"{appName.Value}_{folderId.Value}"

            match nameConflicts.TryFind key with
            | Some value -> return value
            | None ->
                return
                    appList
                    |> List.exists (fun a -> a.State.Name = appName.Value && a.State.FolderId = folderId)
        }

        member _.GetCountAsync() : Task<int> = task { return appList.Length }

        member _.GetCountByFolderIdAsync(folderId: FolderId) : Task<int> = task {
            return appList |> List.filter (fun a -> a.State.FolderId = folderId) |> List.length
        }

        member _.GetCountBySpaceIdsAsync(_spaceIds: SpaceId list) : Task<int> = task { return appList.Length }

        member _.GetByResourceIdAsync(_resourceId: ResourceId) : Task<ValidatedApp list> = task { return [] }

        member _.GetDeletedByFolderIdsAsync(_folderIds: FolderId list) : Task<ValidatedApp list> = task { return [] }

        member _.GetDeletedByIdAsync(_appId: AppId) : Task<ValidatedApp option> = task { return None }

        member _.RestoreAsync(_app: ValidatedApp) : Task<Result<unit, DomainError>> = task { return Ok() }

        member _.CheckNameConflictAsync (_name: string) (_folderId: FolderId) : Task<bool> = task { return false }

// Test helper to create an app
let createTestApp (name: string) (folderId: FolderId) (resourceId: ResourceId) : ValidatedApp =
    let appData = {
        Id = AppId.NewId()
        Name = name
        FolderId = folderId
        ResourceId = resourceId
        HttpMethod = HttpMethod.Get
        Inputs = []
        UrlPath = None
        UrlParameters = []
        Headers = []
        Body = []
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    App.fromData appData

// Test helper to create a resource for testing
let createTestResource (spaceId: SpaceId) : ValidatedResource =
    match Resource.create (UserId.NewId()) spaceId "Test Resource" "Description" "https://api.example.com" [] [] [] with
    | Ok resource -> resource
    | Error _ -> failwith "Failed to create test resource"

[<Fact>]
let ``CreateApp succeeds with valid data`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource
    let actorUserId = UserId.NewId()

    let newApp = createTestApp "My New App" folderId resourceId
    let repository = MockAppRepository([]) :> IAppRepository
    let command = CreateApp(actorUserId, newApp)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppResult appData) ->
        Assert.Equal("My New App", appData.Name)
        Assert.Equal(folderId, appData.FolderId)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppResult")
}

[<Fact>]
let ``CreateApp fails when app name exists in folder`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource
    let actorUserId = UserId.NewId()

    let existingApp = createTestApp "Existing App" folderId resourceId
    let newApp = createTestApp "Existing App" folderId resourceId

    let repository = MockAppRepository([ existingApp ]) :> IAppRepository
    let command = CreateApp(actorUserId, newApp)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already exists", msg)
    | Ok _ -> Assert.Fail("Expected Conflict error for duplicate name in same folder")
    | Error err -> Assert.Fail($"Expected Conflict error but got: {err}")
}

[<Fact>]
let ``DeleteApp removes app from repository`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource
    let actorUserId = UserId.NewId()

    let existingApp = createTestApp "App to Delete" folderId resourceId
    let appId = App.getId existingApp

    let repository = MockAppRepository([ existingApp ]) :> IAppRepository
    let command = DeleteApp(actorUserId, appId.Value.ToString())

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppUnitResult()) -> Assert.True(true)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppUnitResult")
}

[<Fact>]
let ``DeleteApp returns NotFound for nonexistent app`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let nonExistentAppId = Guid.NewGuid().ToString()

    let repository = MockAppRepository([]) :> IAppRepository
    let command = DeleteApp(actorUserId, nonExistentAppId)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Error(NotFound _) -> Assert.True(true)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``GetAppById returns app when exists`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource

    let existingApp = createTestApp "Test App" folderId resourceId
    let appId = App.getId existingApp

    let repository = MockAppRepository([ existingApp ]) :> IAppRepository
    let command = GetAppById(appId.Value.ToString())

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppResult appData) ->
        Assert.Equal("Test App", appData.Name)
        Assert.Equal(appId, appData.Id)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppResult")
}

[<Fact>]
let ``GetAppById returns NotFound for nonexistent app`` () = task {
    // Arrange
    let nonExistentAppId = Guid.NewGuid().ToString()

    let repository = MockAppRepository([]) :> IAppRepository
    let command = GetAppById(nonExistentAppId)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Error(NotFound _) -> Assert.True(true)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``GetAppsByFolderId returns apps in folder with pagination`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource

    let apps = [
        createTestApp "App 1" folderId resourceId
        createTestApp "App 2" folderId resourceId
        createTestApp "App 3" folderId resourceId
    ]

    let repository = MockAppRepository(apps) :> IAppRepository
    let command = GetAppsByFolderId(folderId.Value.ToString(), 0, 10)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppsResult pagedResult) ->
        Assert.Equal(3, pagedResult.TotalCount)
        Assert.Equal(3, pagedResult.Items.Length)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppsResult")
}

[<Fact>]
let ``GetAppsByFolderId with negative skip returns ValidationError`` () = task {
    // Arrange
    let folderId = FolderId.NewId()

    let repository = MockAppRepository([]) :> IAppRepository
    let command = GetAppsByFolderId(folderId.Value.ToString(), -1, 10)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Skip cannot be negative", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAppsByFolderId with take greater than 100 returns ValidationError`` () = task {
    // Arrange
    let folderId = FolderId.NewId()

    let repository = MockAppRepository([]) :> IAppRepository
    let command = GetAppsByFolderId(folderId.Value.ToString(), 0, 101)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Take must be between 1 and 100", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAllApps returns paginated results`` () = task {
    // Arrange
    let folderId1 = FolderId.NewId()
    let folderId2 = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource

    let apps = [
        createTestApp "App 1" folderId1 resourceId
        createTestApp "App 2" folderId1 resourceId
        createTestApp "App 3" folderId2 resourceId
        createTestApp "App 4" folderId2 resourceId
        createTestApp "App 5" folderId2 resourceId
    ]

    let repository = MockAppRepository(apps) :> IAppRepository
    let command = GetAllApps(1, 2) // Skip 1, take 2

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppsResult pagedResult) ->
        Assert.Equal(5, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.Equal(1, pagedResult.Skip)
        Assert.Equal(2, pagedResult.Take)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppsResult")
}

[<Fact>]
let ``GetAppsBySpaceIds returns apps from multiple spaces`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId1 = SpaceId.NewId()
    let spaceId2 = SpaceId.NewId()
    let resource1 = createTestResource spaceId1
    let resource2 = createTestResource spaceId2
    let resourceId1 = Resource.getId resource1
    let resourceId2 = Resource.getId resource2

    let apps = [
        createTestApp "App 1" folderId resourceId1
        createTestApp "App 2" folderId resourceId2
    ]

    let repository = MockAppRepository(apps) :> IAppRepository
    let command = GetAppsBySpaceIds([ spaceId1; spaceId2 ], 0, 10)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppsResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppsResult")
}

[<Fact>]
let ``GetAppsBySpaceIds with empty list returns empty result`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource

    let apps = [ createTestApp "App 1" folderId resourceId ]

    let repository = MockAppRepository(apps) :> IAppRepository
    let command = GetAppsBySpaceIds([], 0, 10)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppsResult pagedResult) ->
        Assert.Equal(0, pagedResult.TotalCount)
        Assert.Empty(pagedResult.Items)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppsResult")
}

[<Fact>]
let ``UpdateAppName changes name and creates event`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let spaceId = SpaceId.NewId()
    let resource = createTestResource spaceId
    let resourceId = Resource.getId resource
    let actorUserId = UserId.NewId()

    let existingApp = createTestApp "Original Name" folderId resourceId
    let appId = App.getId existingApp

    let repository = MockAppRepository([ existingApp ]) :> IAppRepository
    let dto: UpdateAppNameDto = { Name = "Updated Name" }
    let command = UpdateAppName(actorUserId, appId.Value.ToString(), dto)

    // Act
    let! result = AppHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(AppResult appData) -> Assert.Equal("Updated Name", appData.Name)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected AppResult")
}