module Freetool.Application.Tests.TrashHandlerTests

open System
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Commands
open Freetool.Application.Handlers
open Freetool.Application.Interfaces

// Mock App repository for testing
type MockAppRepository(apps: ValidatedApp list, deletedApps: ValidatedApp list) =
    let mutable appList = apps
    let mutable deletedList = deletedApps
    let mutable nameConflicts = Map.empty<string, bool>

    member _.SetNameConflict(name: string, folderId: FolderId, hasConflict: bool) =
        nameConflicts <- nameConflicts.Add($"{name}_{folderId.Value}", hasConflict)

    interface IAppRepository with
        member _.GetByIdAsync(appId: AppId) : Task<ValidatedApp option> = task {
            return appList |> List.tryFind (fun a -> a.State.Id = appId)
        }

        member _.GetByNameAndFolderIdAsync _ _ = task { return None }
        member _.GetByFolderIdAsync _ _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }

        member _.AddAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            appList <- app :: appList
            return Ok()
        }

        member _.UpdateAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            appList <- app :: (appList |> List.filter (fun a -> a.State.Id <> app.State.Id))
            return Ok()
        }

        member _.DeleteAsync _ _ = task { return Ok() }

        member _.ExistsAsync(appId: AppId) = task { return appList |> List.exists (fun a -> a.State.Id = appId) }

        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountAsync() = task { return appList.Length }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetByResourceIdAsync _ = task { return [] }

        member _.GetDeletedByFolderIdsAsync(folderIds: FolderId list) : Task<ValidatedApp list> = task {
            return
                deletedList
                |> List.filter (fun a -> folderIds |> List.contains a.State.FolderId)
        }

        member _.GetDeletedByIdAsync(appId: AppId) : Task<ValidatedApp option> = task {
            return deletedList |> List.tryFind (fun a -> a.State.Id = appId)
        }

        member _.RestoreAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            deletedList <- deletedList |> List.filter (fun a -> a.State.Id <> app.State.Id)
            appList <- app :: appList
            return Ok()
        }

        member _.CheckNameConflictAsync (name: string) (folderId: FolderId) : Task<bool> = task {
            let key = $"{name}_{folderId.Value}"

            match nameConflicts.TryFind key with
            | Some value -> return value
            | None ->
                // Default: check if name exists in the same folder
                return
                    appList
                    |> List.exists (fun a -> a.State.Name = name && a.State.FolderId = folderId)
        }

// Mock Folder repository for testing
type MockFolderRepository(folders: ValidatedFolder list, deletedFolders: ValidatedFolder list) =
    let mutable folderList = folders
    let mutable deletedList = deletedFolders
    let mutable nameConflicts = Map.empty<string, bool>

    member _.SetNameConflict(name: FolderName, parentId: FolderId option, spaceId: SpaceId, hasConflict: bool) =
        let parentKey =
            parentId
            |> Option.map (fun p -> p.Value.ToString())
            |> Option.defaultValue "null"

        nameConflicts <- nameConflicts.Add($"{name.Value}_{parentKey}_{spaceId.Value}", hasConflict)

    interface IFolderRepository with
        member _.GetByIdAsync(folderId: FolderId) : Task<ValidatedFolder option> = task {
            return folderList |> List.tryFind (fun f -> f.State.Id = folderId)
        }

        member _.GetChildrenAsync(folderId: FolderId) : Task<ValidatedFolder list> = task {
            return folderList |> List.filter (fun f -> f.State.ParentId = Some folderId)
        }

        member _.GetRootFoldersAsync _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }

        member _.GetBySpaceAsync (spaceId: SpaceId) (skip: int) (take: int) : Task<ValidatedFolder list> = task {
            return
                folderList
                |> List.filter (fun f -> f.State.SpaceId = spaceId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.AddAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folder :: folderList
            return Ok()
        }

        member _.UpdateAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folder :: (folderList |> List.filter (fun f -> f.State.Id <> folder.State.Id))
            return Ok()
        }

        member _.DeleteAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folderList |> List.filter (fun f -> f.State.Id <> folder.State.Id)
            return Ok()
        }

        member _.ExistsAsync(folderId: FolderId) = task {
            return folderList |> List.exists (fun f -> f.State.Id = folderId)
        }

        member _.ExistsByNameInParentAsync _ _ = task { return false }
        member _.GetCountAsync() = task { return folderList.Length }

        member _.GetCountBySpaceAsync(spaceId: SpaceId) = task {
            return folderList |> List.filter (fun f -> f.State.SpaceId = spaceId) |> List.length
        }

        member _.GetBySpaceIdsAsync (spaceIds: SpaceId list) (_skip: int) (_take: int) : Task<ValidatedFolder list> = task {
            return folderList |> List.filter (fun f -> spaceIds |> List.contains f.State.SpaceId)
        }

        member _.GetCountBySpaceIdsAsync(spaceIds: SpaceId list) : Task<int> = task {
            return
                folderList
                |> List.filter (fun f -> spaceIds |> List.contains f.State.SpaceId)
                |> List.length
        }

        member _.GetRootCountAsync() = task { return 0 }
        member _.GetChildCountAsync _ = task { return 0 }

        member _.GetDeletedBySpaceAsync(spaceId: SpaceId) : Task<ValidatedFolder list> = task {
            return deletedList |> List.filter (fun f -> f.State.SpaceId = spaceId)
        }

        member _.GetDeletedByIdAsync(folderId: FolderId) : Task<ValidatedFolder option> = task {
            return deletedList |> List.tryFind (fun f -> f.State.Id = folderId)
        }

        member _.RestoreWithChildrenAsync(folder: ValidatedFolder) : Task<Result<int, DomainError>> = task {
            deletedList <- deletedList |> List.filter (fun f -> f.State.Id <> folder.State.Id)
            folderList <- folder :: folderList
            return Ok 1
        }

        member _.CheckNameConflictAsync (name: FolderName) (parentId: FolderId option) (spaceId: SpaceId) : Task<bool> = task {
            let parentKey =
                parentId
                |> Option.map (fun p -> p.Value.ToString())
                |> Option.defaultValue "null"

            let key = $"{name.Value}_{parentKey}_{spaceId.Value}"

            match nameConflicts.TryFind key with
            | Some value -> return value
            | None ->
                // Default: check if name exists in the same parent
                return
                    folderList
                    |> List.exists (fun f ->
                        f.State.Name = name && f.State.ParentId = parentId && f.State.SpaceId = spaceId)
        }

// Mock Resource repository for testing
type MockResourceRepository(resources: ValidatedResource list, deletedResources: ValidatedResource list) =
    let mutable resourceList = resources
    let mutable deletedList = deletedResources
    let mutable nameConflicts = Map.empty<string, bool>

    member _.SetNameConflict(name: ResourceName, spaceId: SpaceId, hasConflict: bool) =
        nameConflicts <- nameConflicts.Add($"{name.Value}_{spaceId.Value}", hasConflict)

    interface IResourceRepository with
        member _.GetByIdAsync(resourceId: ResourceId) : Task<ValidatedResource option> = task {
            return resourceList |> List.tryFind (fun r -> r.State.Id = resourceId)
        }

        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceAsync _ _ _ = task { return [] }
        member _.GetCountBySpaceAsync _ = task { return 0 }

        member _.AddAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            resourceList <- resource :: resourceList
            return Ok()
        }

        member _.UpdateAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            resourceList <-
                resource
                :: (resourceList |> List.filter (fun r -> r.State.Id <> resource.State.Id))

            return Ok()
        }

        member _.DeleteAsync _ = task { return Ok() }

        member _.ExistsAsync(resourceId: ResourceId) = task {
            return resourceList |> List.exists (fun r -> r.State.Id = resourceId)
        }

        member _.ExistsByNameAsync _ = task { return false }
        member _.GetCountAsync() = task { return resourceList.Length }

        member _.GetDeletedBySpaceAsync(spaceId: SpaceId) : Task<ValidatedResource list> = task {
            return deletedList |> List.filter (fun r -> r.State.SpaceId = spaceId)
        }

        member _.GetDeletedByIdAsync(resourceId: ResourceId) : Task<ValidatedResource option> = task {
            return deletedList |> List.tryFind (fun r -> r.State.Id = resourceId)
        }

        member _.RestoreAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            deletedList <- deletedList |> List.filter (fun r -> r.State.Id <> resource.State.Id)
            resourceList <- resource :: resourceList
            return Ok()
        }

        member _.CheckNameConflictAsync (name: ResourceName) (spaceId: SpaceId) : Task<bool> = task {
            let key = $"{name.Value}_{spaceId.Value}"

            match nameConflicts.TryFind key with
            | Some value -> return value
            | None ->
                // Default: check if name exists in the same space
                return
                    resourceList
                    |> List.exists (fun r -> r.State.Name = name && r.State.SpaceId = spaceId)
        }

// Test helper to create a folder
let createTestFolder (name: string) (spaceId: SpaceId) (parentId: FolderId option) : ValidatedFolder =
    match Folder.create (UserId.NewId()) name parentId spaceId with
    | Ok folder -> folder
    | Error _ -> failwith "Failed to create test folder"

// Test helper to create a deleted folder
let createDeletedFolder (name: string) (spaceId: SpaceId) (parentId: FolderId option) : ValidatedFolder =
    let folder = createTestFolder name spaceId parentId

    {
        folder with
            State = { folder.State with IsDeleted = true }
    }

// Test helper to create a resource
let createTestResource (name: string) (spaceId: SpaceId) : ValidatedResource =
    match Resource.create (UserId.NewId()) spaceId name "Test Description" "https://api.example.com" [] [] [] with
    | Ok resource -> resource
    | Error _ -> failwith "Failed to create test resource"

// Test helper to create a deleted resource
let createDeletedResource (name: string) (spaceId: SpaceId) : ValidatedResource =
    let resource = createTestResource name spaceId

    {
        resource with
            State = { resource.State with IsDeleted = true }
    }

// Test helper to create an app
let createTestApp (name: string) (folderId: FolderId) (resourceId: ResourceId) : ValidatedApp =
    let httpMethod =
        HttpMethod.Create("GET")
        |> Result.defaultWith (fun _ -> failwith "Invalid HTTP method")

    let appData: AppData = {
        Id = AppId.NewId()
        Name = name
        FolderId = folderId
        ResourceId = resourceId
        HttpMethod = httpMethod
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

    {
        State = appData
        UncommittedEvents = []
    }

// Test helper to create a deleted app
let createDeletedApp (name: string) (folderId: FolderId) (resourceId: ResourceId) : ValidatedApp =
    let app = createTestApp name folderId resourceId

    {
        app with
            State = { app.State with IsDeleted = true }
    }

[<Fact>]
let ``GetTrashBySpace returns deleted items`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()

    let deletedApp = createDeletedApp "Deleted App" folderId resourceId
    let deletedFolder = createDeletedFolder "Deleted Folder" spaceId None
    let deletedResource = createDeletedResource "Deleted Resource" spaceId

    // Create mock folder that the app belongs to
    let folder = createTestFolder "Parent Folder" spaceId None

    let folderWithId = {
        folder with
            State = { folder.State with Id = folderId }
    }

    let appRepository = MockAppRepository([], [ deletedApp ]) :> IAppRepository

    let folderRepository =
        MockFolderRepository([ folderWithId ], [ deletedFolder ]) :> IFolderRepository

    let resourceRepository =
        MockResourceRepository([], [ deletedResource ]) :> IResourceRepository

    let command = GetTrashBySpace(spaceId.Value.ToString(), 0, 100)

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(TrashListResult pagedResult) ->
        Assert.Equal(3, pagedResult.TotalCount)
        Assert.True(pagedResult.Items |> List.exists (fun item -> item.ItemType = "app"))
        Assert.True(pagedResult.Items |> List.exists (fun item -> item.ItemType = "folder"))
        Assert.True(pagedResult.Items |> List.exists (fun item -> item.ItemType = "resource"))
    | Error err -> Assert.Fail($"Expected TrashListResult but got error: {err}")
}

[<Fact>]
let ``GetTrashBySpace returns empty for space with no trash`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()

    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository
    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let command = GetTrashBySpace(spaceId.Value.ToString(), 0, 100)

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(TrashListResult pagedResult) ->
        Assert.Equal(0, pagedResult.TotalCount)
        Assert.Empty(pagedResult.Items)
    | Error err -> Assert.Fail($"Expected empty TrashListResult but got error: {err}")
}

[<Fact>]
let ``GetTrashBySpace paginates results correctly`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()

    let deletedFolders = [
        createDeletedFolder "Folder 1" spaceId None
        createDeletedFolder "Folder 2" spaceId None
        createDeletedFolder "Folder 3" spaceId None
        createDeletedFolder "Folder 4" spaceId None
        createDeletedFolder "Folder 5" spaceId None
    ]

    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = MockFolderRepository([], deletedFolders) :> IFolderRepository
    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let command = GetTrashBySpace(spaceId.Value.ToString(), 1, 2) // Skip 1, take 2

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(TrashListResult pagedResult) ->
        Assert.Equal(5, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.Equal(1, pagedResult.Skip)
        Assert.Equal(2, pagedResult.Take)
    | Error err -> Assert.Fail($"Expected paginated TrashListResult but got error: {err}")
}

[<Fact>]
let ``GetTrashBySpace returns invalid space ID error`` () = task {
    // Arrange
    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository
    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let command = GetTrashBySpace("not-a-valid-guid", 0, 100)

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("Invalid space ID format", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for invalid space ID")
    | Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
}

[<Fact>]
let ``RestoreApp restores deleted app`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resource = createTestResource "Test Resource" spaceId
    let resourceId = Resource.getId resource

    let deletedApp = createDeletedApp "Deleted App" folderId resourceId
    let appId = App.getId deletedApp

    let appRepository = MockAppRepository([], [ deletedApp ]) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository

    let resourceRepository =
        MockResourceRepository([ resource ], []) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreApp(actorUserId, appId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreAppResult restoreResult) ->
        Assert.Equal(appId.Value.ToString(), restoreResult.RestoredId)
        Assert.True(restoreResult.NewName.IsNone) // No name conflict
    | Error err -> Assert.Fail($"Expected RestoreAppResult but got error: {err}")
}

[<Fact>]
let ``RestoreApp returns NotFound for nonexistent app`` () = task {
    // Arrange
    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository
    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let nonExistentAppId = Guid.NewGuid().ToString()
    let command = RestoreApp(actorUserId, nonExistentAppId)

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("App not found", msg)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``RestoreApp renames on conflict`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resource = createTestResource "Test Resource" spaceId
    let resourceId = Resource.getId resource

    // Create an existing app with the same name
    let existingApp = createTestApp "My App" folderId resourceId
    let deletedApp = createDeletedApp "My App" folderId resourceId
    let appId = App.getId deletedApp

    let mockAppRepo = MockAppRepository([ existingApp ], [ deletedApp ])
    // Set name conflict for the original name
    mockAppRepo.SetNameConflict("My App", folderId, true)
    let appRepository = mockAppRepo :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository

    let resourceRepository =
        MockResourceRepository([ resource ], []) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreApp(actorUserId, appId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreAppResult restoreResult) ->
        Assert.True(restoreResult.NewName.IsSome)
        Assert.Contains("Restored", restoreResult.NewName.Value)
    | Error err -> Assert.Fail($"Expected RestoreAppResult with renamed app but got error: {err}")
}

[<Fact>]
let ``RestoreApp auto-restores deleted resource`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let deletedResource = createDeletedResource "Deleted Resource" spaceId
    let resourceId = Resource.getId deletedResource

    let deletedApp = createDeletedApp "Deleted App" folderId resourceId
    let appId = App.getId deletedApp

    let appRepository = MockAppRepository([], [ deletedApp ]) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository
    // Resource is only in deleted list, not active list
    let resourceRepository =
        MockResourceRepository([], [ deletedResource ]) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreApp(actorUserId, appId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreAppResult restoreResult) ->
        Assert.True(restoreResult.AutoRestoredResourceId.IsSome)
        Assert.Equal(resourceId.Value.ToString(), restoreResult.AutoRestoredResourceId.Value)
    | Error err -> Assert.Fail($"Expected RestoreAppResult with auto-restored resource but got error: {err}")
}

[<Fact>]
let ``RestoreFolder restores folder and children`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let deletedFolder = createDeletedFolder "Deleted Folder" spaceId None
    let folderId = Folder.getId deletedFolder

    let appRepository = MockAppRepository([], []) :> IAppRepository

    let folderRepository =
        MockFolderRepository([], [ deletedFolder ]) :> IFolderRepository

    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreFolder(actorUserId, folderId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreFolderResult restoreResult) ->
        Assert.Equal(folderId.Value.ToString(), restoreResult.RestoredId)
        Assert.True(restoreResult.NewName.IsNone) // No name conflict
    | Error err -> Assert.Fail($"Expected RestoreFolderResult but got error: {err}")
}

[<Fact>]
let ``RestoreFolder renames on conflict`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()

    // Create an existing folder with the same name
    let existingFolder = createTestFolder "My Folder" spaceId None
    let deletedFolder = createDeletedFolder "My Folder" spaceId None
    let folderId = Folder.getId deletedFolder
    let folderName = deletedFolder.State.Name

    let mockFolderRepo = MockFolderRepository([ existingFolder ], [ deletedFolder ])
    // Set name conflict for the original name
    mockFolderRepo.SetNameConflict(folderName, None, spaceId, true)

    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = mockFolderRepo :> IFolderRepository
    let resourceRepository = MockResourceRepository([], []) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreFolder(actorUserId, folderId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreFolderResult restoreResult) ->
        Assert.True(restoreResult.NewName.IsSome)
        Assert.Contains("Restored", restoreResult.NewName.Value)
    | Error err -> Assert.Fail($"Expected RestoreFolderResult with renamed folder but got error: {err}")
}

[<Fact>]
let ``RestoreResource restores deleted resource`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let deletedResource = createDeletedResource "Deleted Resource" spaceId
    let resourceId = Resource.getId deletedResource

    let appRepository = MockAppRepository([], []) :> IAppRepository
    let folderRepository = MockFolderRepository([], []) :> IFolderRepository

    let resourceRepository =
        MockResourceRepository([], [ deletedResource ]) :> IResourceRepository

    let actorUserId = UserId.NewId()
    let command = RestoreResource(actorUserId, resourceId.Value.ToString())

    // Act
    let! result = TrashHandler.handleCommand appRepository folderRepository resourceRepository command

    // Assert
    match result with
    | Ok(RestoreResourceResult restoreResult) ->
        Assert.Equal(resourceId.Value.ToString(), restoreResult.RestoredId)
        Assert.True(restoreResult.NewName.IsNone) // No name conflict
    | Error err -> Assert.Fail($"Expected RestoreResourceResult but got error: {err}")
}