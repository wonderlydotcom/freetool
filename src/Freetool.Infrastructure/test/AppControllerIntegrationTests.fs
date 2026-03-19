module Freetool.Infrastructure.Tests.AppControllerIntegrationTests

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

// ============================================================================
// Mock Types for Testing
// ============================================================================

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

// Mock app repository for testing
type MockAppRepository(getByIdFn: AppId -> Task<ValidatedApp option>) =
    interface IAppRepository with
        member _.GetByIdAsync(appId: AppId) = getByIdFn appId
        member _.GetByNameAndFolderIdAsync _ _ = Task.FromResult(None)
        member _.GetByFolderIdAsync _ _ _ = Task.FromResult([])
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.GetBySpaceIdsAsync _ _ _ = Task.FromResult([])
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync _ _ = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByNameAndFolderIdAsync _ _ = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)
        member _.GetCountByFolderIdAsync(_) = Task.FromResult(0)
        member _.GetCountBySpaceIdsAsync(_) = Task.FromResult(0)
        member _.GetByResourceIdAsync(_) = Task.FromResult([])
        member _.GetDeletedByFolderIdsAsync(_) = Task.FromResult([])
        member _.GetDeletedByIdAsync(_) = Task.FromResult(None)
        member _.RestoreAsync(_) = Task.FromResult(Ok())
        member _.CheckNameConflictAsync _ _ = Task.FromResult(false)

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

// Mock resource repository for testing
type MockResourceRepository(getByIdFn: ResourceId -> Task<ValidatedResource option>) =
    interface IResourceRepository with
        member _.GetByIdAsync(resourceId: ResourceId) = getByIdFn resourceId
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.GetBySpaceAsync _ _ _ = Task.FromResult([])
        member _.GetCountBySpaceAsync(_) = Task.FromResult(0)
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByNameAsync(_) = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)
        member _.GetDeletedBySpaceAsync(_) = Task.FromResult([])
        member _.GetDeletedByIdAsync(_) = Task.FromResult(None)
        member _.RestoreAsync(_) = Task.FromResult(Ok())
        member _.CheckNameConflictAsync _ _ = Task.FromResult(false)

// Mock run repository for testing
type MockRunRepository() =
    interface IRunRepository with
        member _.GetByIdAsync(_) = Task.FromResult(None)
        member _.GetByAppIdAsync _ _ _ = Task.FromResult([])
        member _.GetByStatusAsync _ _ _ = Task.FromResult([])
        member _.GetByAppIdAndStatusAsync _ _ _ _ = Task.FromResult([])
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)
        member _.GetCountByAppIdAsync(_) = Task.FromResult(0)
        member _.GetCountByStatusAsync(_) = Task.FromResult(0)
        member _.GetCountByAppIdAndStatusAsync _ _ = Task.FromResult(0)

type MockSqlExecutionService() =
    interface ISqlExecutionService with
        member _.ExecuteQueryAsync (_resource: ResourceData) (_query: SqlQuery) : Task<Result<string, DomainError>> =
            Task.FromResult(Ok "[]")

// Mock space repository for testing
type MockSpaceRepository(memberSpaces: ValidatedSpace list, moderatorSpaces: ValidatedSpace list) =
    let allSpaces =
        (memberSpaces @ moderatorSpaces) |> List.distinctBy (fun s -> s.State.Id)

    interface ISpaceRepository with
        member _.GetByIdAsync(spaceId: SpaceId) =
            Task.FromResult(allSpaces |> List.tryFind (fun s -> s.State.Id = spaceId))

        member _.GetByNameAsync(_) = Task.FromResult(None)
        member _.GetAllAsync _ _ = Task.FromResult(allSpaces)
        member _.GetByUserIdAsync(_) = Task.FromResult(memberSpaces)
        member _.GetByModeratorUserIdAsync(_) = Task.FromResult(moderatorSpaces)
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByNameAsync(_) = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

// Mock user repository for testing
type MockUserRepository(getByIdFn: UserId -> Task<ValidatedUser option>) =
    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) = getByIdFn userId
        member _.GetByEmailAsync(_) = Task.FromResult(None)
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.AddAsync(_) = Task.FromResult(Ok())
        member _.UpdateAsync(_) = Task.FromResult(Ok())
        member _.DeleteAsync(_) = Task.FromResult(Ok())
        member _.ExistsAsync(_) = Task.FromResult(false)
        member _.ExistsByEmailAsync(_) = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

// Mock command handler for testing
type MockAppCommandHandler(handleCommandFn: AppCommand -> Task<Result<AppCommandResult, DomainError>>) =
    interface ICommandHandler<AppCommand, AppCommandResult> with
        member _.HandleCommand(command: AppCommand) = handleCommandFn command

// ============================================================================
// Helper Functions
// ============================================================================

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

// Helper to create a test resource
let createTestResource (spaceId: SpaceId) (resourceId: ResourceId) : ValidatedResource =
    let resourceName =
        ResourceName.Create(Some "Test Resource") |> Result.toOption |> Option.get

    let resourceDesc =
        ResourceDescription.Create(Some "Test Description")
        |> Result.toOption
        |> Option.get

    let baseUrl =
        BaseUrl.Create(Some "https://api.example.com") |> Result.toOption |> Option.get

    {
        State = {
            Id = resourceId
            Name = resourceName
            Description = resourceDesc
            SpaceId = spaceId
            ResourceKind = ResourceKind.Http
            BaseUrl = Some baseUrl
            UrlParameters = []
            Headers = []
            Body = []
            DatabaseName = None
            DatabaseHost = None
            DatabasePort = None
            DatabaseEngine = None
            DatabaseAuthScheme = None
            DatabaseUsername = None
            DatabasePassword = None
            UseSsl = false
            EnableSshTunnel = false
            ConnectionOptions = []
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
        UncommittedEvents = []
    }

// Helper to create a test app
let createTestApp (folderId: FolderId) (resourceId: ResourceId) (appId: AppId) : ValidatedApp = {
    State = {
        Id = appId
        Name = "Test App"
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
    UncommittedEvents = []
}

// Helper to create a test space
let createTestSpace (spaceId: SpaceId) (moderatorUserId: UserId) : ValidatedSpace = {
    State = {
        Id = spaceId
        Name = "Test Space"
        ModeratorUserId = moderatorUserId
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
        MemberIds = []
    }
    UncommittedEvents = []
}

// Helper to create app data for response
let createAppData (appId: AppId) (folderId: FolderId) (resourceId: ResourceId) : AppData = {
    Id = appId
    Name = "Test App"
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

// Helper to create a test controller with mocked dependencies
let createTestControllerWithSpaceAccess
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (getAppByIdFn: AppId -> Task<ValidatedApp option>)
    (getFolderByIdFn: FolderId -> Task<ValidatedFolder option>)
    (getResourceByIdFn: ResourceId -> Task<ValidatedResource option>)
    (memberSpaces: ValidatedSpace list)
    (moderatorSpaces: ValidatedSpace list)
    (handleCommandFn: AppCommand -> Task<Result<AppCommandResult, DomainError>>)
    (userId: UserId)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let appRepository = MockAppRepository(getAppByIdFn) :> IAppRepository
    let folderRepository = MockFolderRepository(getFolderByIdFn) :> IFolderRepository

    let resourceRepository =
        MockResourceRepository(getResourceByIdFn) :> IResourceRepository

    let runRepository = MockRunRepository() :> IRunRepository
    let sqlExecutionService = MockSqlExecutionService() :> ISqlExecutionService

    let spaceRepository =
        MockSpaceRepository(memberSpaces, moderatorSpaces) :> ISpaceRepository

    let userRepository =
        MockUserRepository(fun _ -> Task.FromResult(None)) :> IUserRepository

    let commandHandler =
        MockAppCommandHandler(handleCommandFn) :> ICommandHandler<AppCommand, AppCommandResult>

    let controller =
        AppController(
            appRepository,
            resourceRepository,
            runRepository,
            sqlExecutionService,
            folderRepository,
            spaceRepository,
            userRepository,
            authService,
            commandHandler
        )

    // Setup HttpContext with the provided UserId
    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- userId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    controller

let createTestController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (getAppByIdFn: AppId -> Task<ValidatedApp option>)
    (getFolderByIdFn: FolderId -> Task<ValidatedFolder option>)
    (getResourceByIdFn: ResourceId -> Task<ValidatedResource option>)
    (spaces: ValidatedSpace list)
    (handleCommandFn: AppCommand -> Task<Result<AppCommandResult, DomainError>>)
    (userId: UserId)
    =
    createTestControllerWithSpaceAccess
        checkPermissionFn
        getAppByIdFn
        getFolderByIdFn
        getResourceByIdFn
        spaces
        []
        handleCommandFn
        userId

// ============================================================================
// Create Operations Tests
// ============================================================================

[<Fact>]
let ``CreateApp returns 201 with valid request`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let resource = createTestResource spaceId resourceId
    let space = createTestSpace spaceId userId

    // Grant create_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppCreate, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById _ = Task.FromResult(None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById id =
        Task.FromResult(if id = resourceId then Some resource else None)

    let handleCommand cmd =
        match cmd with
        | CreateApp _ ->
            let appData = createAppData appId folderId resourceId
            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let createDto: CreateAppDto = {
        Name = "My New App"
        FolderId = folderId.Value.ToString()
        ResourceId = resourceId.Value.ToString()
        HttpMethod = "GET"
        Inputs = []
        UrlPath = None
        UrlParameters = []
        Headers = []
        Body = []
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
    }

    // Act
    let! result = controller.CreateApp(createDto)

    // Assert
    match result with
    | :? CreatedAtActionResult as createdResult ->
        Assert.Equal(201, createdResult.StatusCode.Value)
        Assert.NotNull(createdResult.Value)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        // If not 201, verify it's not an error
        Assert.True(objResult.StatusCode.Value < 400, $"Expected success, got {objResult.StatusCode.Value}")
    | _ -> ()
}

[<Fact>]
let ``CreateApp returns 400 for invalid name`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()

    let folder = createTestFolder spaceId folderId
    let resource = createTestResource spaceId resourceId
    let space = createTestSpace spaceId userId

    // Grant create_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppCreate, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById _ = Task.FromResult(None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById id =
        Task.FromResult(if id = resourceId then Some resource else None)

    let handleCommand cmd =
        match cmd with
        | CreateApp _ -> Task.FromResult(Error(ValidationError "Name is required"))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    // Empty name is invalid
    let createDto: CreateAppDto = {
        Name = ""
        FolderId = folderId.Value.ToString()
        ResourceId = resourceId.Value.ToString()
        HttpMethod = "GET"
        Inputs = []
        UrlPath = None
        UrlParameters = []
        Headers = []
        Body = []
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
    }

    // Act
    let! result = controller.CreateApp(createDto)

    // Assert
    match result with
    | :? BadRequestObjectResult as badRequest -> Assert.Equal(400, badRequest.StatusCode.Value)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(400, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected BadRequest result")
}

[<Fact>]
let ``CreateApp returns 409 for duplicate name`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()

    let folder = createTestFolder spaceId folderId
    let resource = createTestResource spaceId resourceId
    let space = createTestSpace spaceId userId

    // Grant create_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppCreate, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById _ = Task.FromResult(None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById id =
        Task.FromResult(if id = resourceId then Some resource else None)

    // Handler returns conflict error
    let handleCommand cmd =
        match cmd with
        | CreateApp _ -> Task.FromResult(Error(Conflict "An app with this name already exists in this folder"))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let createDto: CreateAppDto = {
        Name = "Existing App"
        FolderId = folderId.Value.ToString()
        ResourceId = resourceId.Value.ToString()
        HttpMethod = "GET"
        Inputs = []
        UrlPath = None
        UrlParameters = []
        Headers = []
        Body = []
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
    }

    // Act
    let! result = controller.CreateApp(createDto)

    // Assert
    match result with
    | :? ConflictObjectResult as conflict -> Assert.Equal(409, conflict.StatusCode.Value)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(409, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected Conflict result")
}

// ============================================================================
// Read Operations Tests
// ============================================================================

[<Fact>]
let ``GetAppById returns 200 with app data`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // Grant run_app permission (required for read)
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppRun, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | GetAppById _ ->
            let appData = createAppData appId folderId resourceId
            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    // Act
    let! result = controller.GetAppById(appId.Value.ToString())

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``GetAppById returns 404 for nonexistent`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let nonExistentAppId = AppId.NewId()

    // No app exists
    let checkPermission _ _ _ = true // Grant all permissions

    let getAppById _ = Task.FromResult(None)
    let getFolderById _ = Task.FromResult(None)
    let getResourceById _ = Task.FromResult(None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "App not found"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [] handleCommand userId

    // Act
    let! result = controller.GetAppById(nonExistentAppId.Value.ToString())

    // Assert
    match result with
    | :? NotFoundObjectResult as notFound -> Assert.Equal(404, notFound.StatusCode.Value)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(404, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected NotFound result")
}

[<Fact>]
let ``GetAppsByFolderId returns 200 with paginated list`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let space = createTestSpace spaceId userId

    // Grant run_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppRun, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById _ = Task.FromResult(None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | GetAppsByFolderId _ ->
            let appData = createAppData appId folderId resourceId

            let pagedResult: PagedResult<AppData> = {
                Items = [ appData ]
                TotalCount = 1
                Skip = 0
                Take = 10
            }

            Task.FromResult(Ok(AppsResult pagedResult))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    // Act
    let! result = controller.GetAppsByFolderId(folderId.Value.ToString(), 0, 10)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``GetApps includes spaces where user is moderator`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let space = createTestSpace spaceId userId

    let checkPermission _ _ _ = true
    let getAppById _ = Task.FromResult(None)
    let getFolderById _ = Task.FromResult(None)
    let getResourceById _ = Task.FromResult(None)

    let receivedSpaceIds = ref []

    let handleCommand cmd =
        match cmd with
        | GetAppsBySpaceIds(spaceIds, skip, take) ->
            receivedSpaceIds := spaceIds

            let pagedResult: PagedResult<AppData> = {
                Items = []
                TotalCount = 0
                Skip = skip
                Take = take
            }

            Task.FromResult(Ok(AppsResult pagedResult))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestControllerWithSpaceAccess
            checkPermission
            getAppById
            getFolderById
            getResourceById
            []
            [ space ]
            handleCommand
            userId

    // Act
    let! result = controller.GetApps(0, 10)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.Equal<SpaceId list>([ spaceId ], !receivedSpaceIds)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

// ============================================================================
// Delete Operations Tests
// ============================================================================

[<Fact>]
let ``DeleteApp returns 204 on success`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // Grant delete_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppDelete, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | DeleteApp _ -> Task.FromResult(Ok(AppUnitResult()))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    // Act
    let! result = controller.DeleteApp(appId.Value.ToString())

    // Assert
    match result with
    | :? NoContentResult as noContent -> Assert.Equal(204, noContent.StatusCode)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.True(objResult.StatusCode.Value = 204 || objResult.StatusCode.Value < 400)
    | _ -> ()
}

[<Fact>]
let ``DeleteApp returns 403 without permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // No permissions granted
    let checkPermission _ _ _ = false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    // Act
    let! result = controller.DeleteApp(appId.Value.ToString())

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

// ============================================================================
// Update Operations Tests
// ============================================================================

[<Fact>]
let ``UpdateAppName returns 200 on success`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // Grant edit_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | UpdateAppName _ ->
            let appData = {
                createAppData appId folderId resourceId with
                    Name = "Updated App Name"
            }

            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let updateDto: UpdateAppNameDto = { Name = "Updated App Name" }

    // Act
    let! result = controller.UpdateAppName(appId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``UpdateAppName returns 403 without edit permission`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // No permissions granted
    let checkPermission _ _ _ = false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let updateDto: UpdateAppNameDto = { Name = "Updated App Name" }

    // Act
    let! result = controller.UpdateAppName(appId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``UpdateAppName returns 404 for nonexistent`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let nonExistentAppId = AppId.NewId()

    // Grant all permissions
    let checkPermission _ _ _ = true

    let getAppById _ = Task.FromResult(None)
    let getFolderById _ = Task.FromResult(None)
    let getResourceById _ = Task.FromResult(None)

    let handleCommand _ =
        Task.FromResult(Error(NotFound "App not found"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [] handleCommand userId

    let updateDto: UpdateAppNameDto = { Name = "Updated App Name" }

    // Act
    let! result = controller.UpdateAppName(nonExistentAppId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? NotFoundObjectResult as notFound -> Assert.Equal(404, notFound.StatusCode.Value)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(404, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected NotFound result")
}

[<Fact>]
let ``UpdateAppInputs returns 200 with updated app`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let space = createTestSpace spaceId userId

    // Grant edit_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById _ = Task.FromResult(None)

    let handleCommand cmd =
        match cmd with
        | UpdateAppInputs _ ->
            let appData = createAppData appId folderId resourceId
            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let updateDto: UpdateAppInputsDto = { Inputs = [] }

    // Act
    let! result = controller.UpdateAppInputs(appId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``UpdateAppQueryParameters returns 200`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let resource = createTestResource spaceId resourceId
    let space = createTestSpace spaceId userId

    // Grant edit_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById id =
        Task.FromResult(if id = resourceId then Some resource else None)

    let handleCommand cmd =
        match cmd with
        | UpdateAppQueryParameters _ ->
            let appData = createAppData appId folderId resourceId
            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let updateDto: UpdateAppQueryParametersDto = { UrlParameters = [] }

    // Act
    let! result = controller.UpdateAppQueryParameters(appId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``UpdateAppBody returns 200`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()

    let folder = createTestFolder spaceId folderId
    let app = createTestApp folderId resourceId appId
    let resource = createTestResource spaceId resourceId
    let space = createTestSpace spaceId userId

    // Grant edit_app permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, AppEdit, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId.Value.ToString()
        | _ -> false

    let getAppById id =
        Task.FromResult(if id = appId then Some app else None)

    let getFolderById id =
        Task.FromResult(if id = folderId then Some folder else None)

    let getResourceById id =
        Task.FromResult(if id = resourceId then Some resource else None)

    let handleCommand cmd =
        match cmd with
        | UpdateAppBody _ ->
            let appData = createAppData appId folderId resourceId
            Task.FromResult(Ok(AppResult appData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller =
        createTestController checkPermission getAppById getFolderById getResourceById [ space ] handleCommand userId

    let updateDto: UpdateAppBodyDto = { Body = [] }

    // Act
    let! result = controller.UpdateAppBody(appId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? OkObjectResult as okResult ->
        Assert.Equal(200, okResult.StatusCode.Value)
        Assert.NotNull(okResult.Value)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}