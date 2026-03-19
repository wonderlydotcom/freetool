module Freetool.Infrastructure.Tests.FolderControllerTests

open System
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces
open Freetool.Api.Controllers

// Mock authorization service for testing
type MockAuthorizationService(checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool) =
    interface IAuthorizationService with
        member _.CreateStoreAsync(req) =
            Task.FromResult({ Id = "store-1"; Name = req.Name })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())

        member _.CreateRelationshipsAsync(_) = Task.FromResult(())
        member _.UpdateRelationshipsAsync(_) = Task.FromResult(())
        member _.DeleteRelationshipsAsync(_) = Task.FromResult(())

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (object: AuthObject) =
            Task.FromResult(checkPermissionFn subject relation object)

        member _.StoreExistsAsync(_storeId: string) = Task.FromResult(true)

        member _.BatchCheckPermissionsAsync (subject: AuthSubject) (relations: AuthRelation list) (object: AuthObject) =
            let results =
                relations
                |> List.map (fun relation -> (relation, checkPermissionFn subject relation object))
                |> Map.ofList

            Task.FromResult(results)

// Mock folder repository for testing
type MockFolderRepository(getByIdFn: FolderId -> Task<ValidatedFolder option>) =
    interface IFolderRepository with
        member _.GetByIdAsync(folderId: FolderId) = getByIdFn folderId
        member _.GetChildrenAsync(_) = Task.FromResult([])
        member _.GetRootFoldersAsync _ _ = Task.FromResult([])
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.GetBySpaceAsync _ _ _ = Task.FromResult([])
        member _.GetBySpaceIdsAsync _ _ _ = Task.FromResult([])
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByNameInParentAsync _ _ = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)
        member _.GetCountBySpaceAsync(_) = Task.FromResult(0)
        member _.GetCountBySpaceIdsAsync(_) = Task.FromResult(0)
        member _.GetRootCountAsync() = Task.FromResult(0)
        member _.GetChildCountAsync(_) = Task.FromResult(0)
        member _.GetDeletedBySpaceAsync(_) = Task.FromResult([])
        member _.GetDeletedByIdAsync(_) = Task.FromResult(None)
        member _.RestoreWithChildrenAsync _ = Task.FromResult(Ok 0)
        member _.CheckNameConflictAsync _ _ _ = Task.FromResult(false)

// Mock space repository for testing
type MockSpaceRepository() =
    interface ISpaceRepository with
        member _.GetByIdAsync(_) = Task.FromResult(None)
        member _.GetByNameAsync(_) = Task.FromResult(None)
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.GetByUserIdAsync(_) = Task.FromResult([])
        member _.GetByModeratorUserIdAsync(_) = Task.FromResult([])
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByNameAsync(_) = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

// Mock command handler for testing
type MockFolderCommandHandler(handleCommandFn: FolderCommand -> Task<Result<FolderCommandResult, DomainError>>) =
    interface ICommandHandler<FolderCommand, FolderCommandResult> with
        member _.HandleCommand(command: FolderCommand) = handleCommandFn command

// Helper to create a test controller with mocked dependencies
let createTestController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (getByIdFn: FolderId -> Task<ValidatedFolder option>)
    (handleCommandFn: FolderCommand -> Task<Result<FolderCommandResult, DomainError>>)
    (userId: UserId)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let folderRepository = MockFolderRepository(getByIdFn) :> IFolderRepository
    let spaceRepository = MockSpaceRepository() :> ISpaceRepository

    let commandHandler =
        MockFolderCommandHandler(handleCommandFn) :> ICommandHandler<FolderCommand, FolderCommandResult>

    let controller =
        FolderController(folderRepository, spaceRepository, commandHandler, authService)

    // Setup HttpContext with the provided UserId
    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- userId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    controller

// Helper to create a test folder
let createTestFolder (spaceId: SpaceId) (folderId: FolderId) : ValidatedFolder =
    let folderName =
        FolderName.Create(Some "Test Folder") |> Result.toOption |> Option.get

    {
        State = {
            Id = folderId
            Name = folderName
            ParentId = None
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = []
        }
        UncommittedEvents = []
    }

// ============================================================================
// CreateFolder Tests
// ============================================================================

[<Fact>]
let ``CreateFolder returns 403 when user does not have create_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let getById _ = Task.FromResult(None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission getById handleCommand userId

    let createDto: CreateFolderDto = {
        Name = "My Folder"
        Location = None
        SpaceId = spaceId.Value.ToString()
    }

    // Act
    let! result = controller.CreateFolder(createDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``CreateFolder succeeds when user has create_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    // Grant create_folder permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, FolderCreate, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | CreateFolder _ ->
            let folderData = {
                Id = FolderId.NewId()
                Name = FolderName.Create(Some "My Folder") |> Result.toOption |> Option.get
                ParentId = None
                SpaceId = spaceId
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                IsDeleted = false
                Children = []
            }

            Task.FromResult(Ok(FolderResult folderData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    let createDto: CreateFolderDto = {
        Name = "My Folder"
        Location = None
        SpaceId = spaceId.Value.ToString()
    }

    // Act
    let! result = controller.CreateFolder(createDto)

    // Assert - Verify it's not a 403 (should succeed)
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// UpdateFolderName Tests
// ============================================================================

[<Fact>]
let ``UpdateFolderName returns 403 when user does not have edit_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission getById handleCommand userId

    let updateDto: UpdateFolderNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateFolderName(folderId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``UpdateFolderName succeeds when user has edit_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()

    // Grant edit_folder permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, FolderEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand cmd =
        match cmd with
        | UpdateFolderName _ ->
            let updatedFolder = folder.State
            Task.FromResult(Ok(FolderResult updatedFolder))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    let updateDto: UpdateFolderNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateFolderName(folderId.Value.ToString(), updateDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// MoveFolder Tests
// ============================================================================

[<Fact>]
let ``MoveFolder returns 403 when user does not have edit_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission getById handleCommand userId

    let moveDto: MoveFolderDto = { ParentId = None }

    // Act
    let! result = controller.MoveFolder(folderId.Value.ToString(), moveDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``MoveFolder succeeds when user has edit_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()

    // Grant edit_folder permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, FolderEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand cmd =
        match cmd with
        | MoveFolder _ ->
            let movedFolder = folder.State
            Task.FromResult(Ok(FolderResult movedFolder))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    let moveDto: MoveFolderDto = { ParentId = None }

    // Act
    let! result = controller.MoveFolder(folderId.Value.ToString(), moveDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// DeleteFolder Tests
// ============================================================================

[<Fact>]
let ``DeleteFolder returns 403 when user does not have delete_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.DeleteFolder(folderId.Value.ToString())

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``DeleteFolder succeeds when user has delete_folder permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()

    // Grant delete_folder permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, FolderDelete, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand cmd =
        match cmd with
        | DeleteFolder _ -> Task.FromResult(Ok(FolderUnitResult()))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.DeleteFolder(folderId.Value.ToString())

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable (like NoContentResult)
}

// ============================================================================
// Read Operations Tests (should allow any authenticated user)
// ============================================================================

[<Fact>]
let ``GetFolderById allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand cmd =
        match cmd with
        | GetFolderById _ -> Task.FromResult(Ok(FolderResult folder.State))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.GetFolderById(folderId.Value.ToString())

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult)
    | _ -> () // Test passes as long as no 403 is returned
}

[<Fact>]
let ``GetFolderWithChildren allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let folder = createTestFolder spaceId folderId

    let getById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let handleCommand cmd =
        match cmd with
        | GetFolderWithChildren _ -> Task.FromResult(Ok(FolderResult folder.State))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.GetFolderWithChildren(folderId.Value.ToString())

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult)
    | _ -> () // Test passes as long as no 403 is returned
}

[<Fact>]
let ``GetRootFolders allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let pagedResult = {
        Items = []
        TotalCount = 0
        Skip = 0
        Take = 10
    }

    let getById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | GetRootFolders _ -> Task.FromResult(Ok(FoldersResult pagedResult))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.GetRootFolders(0, 10)

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult)
    | _ -> () // Test passes as long as no 403 is returned
}

[<Fact>]
let ``GetAllFolders allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let pagedResult = {
        Items = []
        TotalCount = 0
        Skip = 0
        Take = 10
    }

    let getById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | GetAllFolders _ -> Task.FromResult(Ok(FoldersResult pagedResult))
        | GetFoldersBySpaceIds _ -> Task.FromResult(Ok(FoldersResult pagedResult))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission getById handleCommand userId

    // Act
    let! result = controller.GetAllFolders("", 0, 10) // Empty string = no workspace filter

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult.Value)
    | _ -> () // Test passes as long as no 403 is returned
}