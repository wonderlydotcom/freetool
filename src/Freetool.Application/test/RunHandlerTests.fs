module Freetool.Application.Tests.RunHandlerTests

open System
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Application.Commands
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Application.DTOs

// Mock Run repository for testing
type MockRunRepository(runs: ValidatedRun list) =
    let mutable runList = runs

    interface IRunRepository with
        member _.GetByIdAsync(runId: RunId) : Task<ValidatedRun option> = task {
            return runList |> List.tryFind (fun r -> r.State.Id = runId)
        }

        member _.GetByAppIdAsync (appId: AppId) (skip: int) (take: int) : Task<ValidatedRun list> = task {
            return
                runList
                |> List.filter (fun r -> r.State.AppId = appId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetByStatusAsync (status: RunStatus) (skip: int) (take: int) : Task<ValidatedRun list> = task {
            return
                runList
                |> List.filter (fun r -> r.State.Status = status)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetByAppIdAndStatusAsync
            (appId: AppId)
            (status: RunStatus)
            (skip: int)
            (take: int)
            : Task<ValidatedRun list> =
            task {
                return
                    runList
                    |> List.filter (fun r -> r.State.AppId = appId && r.State.Status = status)
                    |> List.skip skip
                    |> List.truncate take
            }

        member _.AddAsync(run: ValidatedRun) : Task<Result<unit, DomainError>> = task {
            runList <- run :: runList
            return Ok()
        }

        member _.UpdateAsync(run: ValidatedRun) : Task<Result<unit, DomainError>> = task {
            runList <- run :: (runList |> List.filter (fun r -> r.State.Id <> run.State.Id))
            return Ok()
        }

        member _.ExistsAsync(runId: RunId) : Task<bool> = task {
            return runList |> List.exists (fun r -> r.State.Id = runId)
        }

        member _.GetCountAsync() : Task<int> = task { return runList.Length }

        member _.GetCountByAppIdAsync(appId: AppId) : Task<int> = task {
            return runList |> List.filter (fun r -> r.State.AppId = appId) |> List.length
        }

        member _.GetCountByStatusAsync(status: RunStatus) : Task<int> = task {
            return runList |> List.filter (fun r -> r.State.Status = status) |> List.length
        }

        member _.GetCountByAppIdAndStatusAsync (appId: AppId) (status: RunStatus) : Task<int> = task {
            return
                runList
                |> List.filter (fun r -> r.State.AppId = appId && r.State.Status = status)
                |> List.length
        }

type MockSqlExecutionService() =
    interface ISqlExecutionService with
        member _.ExecuteQueryAsync (_resource: ResourceData) (_query: SqlQuery) : Task<Result<string, DomainError>> = task {
            return Ok "[]"
        }

let sqlExecutionService = MockSqlExecutionService() :> ISqlExecutionService

// Mock App repository for testing
type MockAppRepository(apps: ValidatedApp list) =
    let mutable appList = apps

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
        member _.GetDeletedByFolderIdsAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

// Mock Resource repository for testing
type MockResourceRepository(resources: ValidatedResource list) =
    let mutable resourceList = resources

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

        member _.DeleteAsync(_resource: ValidatedResource) = task { return Ok() }

        member _.ExistsAsync(resourceId: ResourceId) = task {
            return resourceList |> List.exists (fun r -> r.State.Id = resourceId)
        }

        member _.ExistsByNameAsync _ = task { return false }
        member _.GetCountAsync() = task { return resourceList.Length }
        member _.GetDeletedBySpaceAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

// Test helper to create a resource
let createTestResource (name: string) (spaceId: SpaceId) : ValidatedResource =
    match Resource.create (UserId.NewId()) spaceId name "Test Description" "https://api.example.com" [] [] [] with
    | Ok resource -> resource
    | Error _ -> failwith "Failed to create test resource"

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

// Test helper to create a run
let createTestRun (appId: AppId) (status: RunStatus) : ValidatedRun =
    let runData: RunData = {
        Id = RunId.NewId()
        AppId = appId
        Status = status
        InputValues = []
        ExecutableRequest = None
        ExecutedSql = None
        Response = None
        ErrorMessage = None
        StartedAt = None
        CompletedAt = None
        CreatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    {
        State = runData
        UncommittedEvents = []
    }

// Create current user for testing
let testCurrentUser: CurrentUser = {
    Id = "user-123"
    Email = "test@example.com"
    FirstName = "Test"
    LastName = "User"
}

[<Fact>]
let ``CreateRun succeeds with valid app and inputs`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resource = createTestResource "Test Resource" spaceId
    let resourceId = Resource.getId resource
    let app = createTestApp "Test App" folderId resourceId
    let appId = App.getId app

    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([ app ]) :> IAppRepository
    let resourceRepository = MockResourceRepository([ resource ]) :> IResourceRepository

    let createRunDto: CreateRunDto = { InputValues = []; DynamicBody = None }
    let actorUserId = UserId.NewId()

    let command =
        CreateRun(actorUserId, appId.Value.ToString(), testCurrentUser, createRunDto)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunResult runData) ->
        Assert.Equal(appId, runData.AppId)
        Assert.True(runData.Status = Success || runData.Status = Failure) // Will fail due to mock HTTP
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
}

[<Fact>]
let ``CreateRun fails for nonexistent app`` () = task {
    // Arrange
    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let createRunDto: CreateRunDto = { InputValues = []; DynamicBody = None }
    let actorUserId = UserId.NewId()
    let nonExistentAppId = Guid.NewGuid().ToString()

    let command =
        CreateRun(actorUserId, nonExistentAppId, testCurrentUser, createRunDto)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("App not found", msg)
    | Ok _ -> Assert.Fail("Expected NotFound error for nonexistent app")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``CreateRun fails when resource not found`` () = task {
    // Arrange
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let app = createTestApp "Test App" folderId resourceId
    let appId = App.getId app

    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([ app ]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository // No resource

    let createRunDto: CreateRunDto = { InputValues = []; DynamicBody = None }
    let actorUserId = UserId.NewId()

    let command =
        CreateRun(actorUserId, appId.Value.ToString(), testCurrentUser, createRunDto)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunResult runData) ->
        // Run is created but marked with InvalidConfiguration status
        Assert.Equal(InvalidConfiguration, runData.Status)
        Assert.True(runData.ErrorMessage.IsSome)
        Assert.Contains("resource not found", runData.ErrorMessage.Value.ToLower())
    | Error err -> Assert.Fail($"Expected InvalidConfiguration run but got error: {err}")
}

[<Fact>]
let ``CreateRun marks as invalid when input validation fails`` () = task {
    // Arrange
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resource = createTestResource "Test Resource" spaceId
    let resourceId = Resource.getId resource

    // Create app with required input
    let httpMethod =
        HttpMethod.Create("GET")
        |> Result.defaultWith (fun _ -> failwith "Invalid HTTP method")

    let inputType =
        InputType.Text(100)
        |> Result.defaultWith (fun _ -> failwith "Invalid input type")

    let requiredInput: Input = {
        Title = "RequiredField"
        Description = None
        Type = inputType
        Required = true
        DefaultValue = None
    }

    let appData: AppData = {
        Id = AppId.NewId()
        Name = "Test App"
        FolderId = folderId
        ResourceId = resourceId
        HttpMethod = httpMethod
        Inputs = [ requiredInput ]
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

    let app: ValidatedApp = {
        State = appData
        UncommittedEvents = []
    }

    let appId = App.getId app

    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([ app ]) :> IAppRepository
    let resourceRepository = MockResourceRepository([ resource ]) :> IResourceRepository

    // Create run without the required input
    let createRunDto: CreateRunDto = { InputValues = []; DynamicBody = None }
    let actorUserId = UserId.NewId()

    let command =
        CreateRun(actorUserId, appId.Value.ToString(), testCurrentUser, createRunDto)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Error(ValidationError msg) ->
        Assert.Contains("RequiredField", msg)
        Assert.Contains("Missing required inputs", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for missing required input")
    | Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
}

[<Fact>]
let ``GetRunById returns run when exists`` () = task {
    // Arrange
    let appId = AppId.NewId()
    let run = createTestRun appId Pending
    let runId = Run.getId run

    let runRepository = MockRunRepository([ run ]) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunById(runId.Value.ToString())

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunResult runData) -> Assert.Equal(runId, runData.Id)
    | Error err -> Assert.Fail($"Expected RunResult but got error: {err}")
}

[<Fact>]
let ``GetRunById returns NotFound for nonexistent run`` () = task {
    // Arrange
    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let nonExistentRunId = Guid.NewGuid().ToString()
    let command = GetRunById(nonExistentRunId)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("Run not found", msg)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``GetRunsByAppId returns paginated runs`` () = task {
    // Arrange
    let appId = AppId.NewId()

    let runs = [
        createTestRun appId Pending
        createTestRun appId Running
        createTestRun appId Success
    ]

    let runRepository = MockRunRepository(runs) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunsByAppId(appId.Value.ToString(), 0, 10)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunsResult pagedResult) ->
        Assert.Equal(3, pagedResult.TotalCount)
        Assert.Equal(3, pagedResult.Items.Length)
        Assert.All(pagedResult.Items, fun item -> Assert.Equal(appId, item.AppId))
    | Error err -> Assert.Fail($"Expected RunsResult but got error: {err}")
}

[<Fact>]
let ``GetRunsByStatus returns runs with matching status`` () = task {
    // Arrange
    let appId1 = AppId.NewId()
    let appId2 = AppId.NewId()

    let runs = [
        createTestRun appId1 Pending
        createTestRun appId1 Success
        createTestRun appId2 Pending
        createTestRun appId2 Failure
    ]

    let runRepository = MockRunRepository(runs) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunsByStatus("pending", 0, 10)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunsResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.All(pagedResult.Items, fun item -> Assert.Equal(Pending, item.Status))
    | Error err -> Assert.Fail($"Expected RunsResult but got error: {err}")
}

[<Fact>]
let ``GetRunsByStatus fails for invalid status string`` () = task {
    // Arrange
    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunsByStatus("invalid_status_xyz", 0, 10)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("Invalid run status", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for invalid status")
    | Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
}

[<Fact>]
let ``GetRunsByAppIdAndStatus combines filters`` () = task {
    // Arrange
    let appId1 = AppId.NewId()
    let appId2 = AppId.NewId()

    let runs = [
        createTestRun appId1 Pending
        createTestRun appId1 Success
        createTestRun appId1 Pending
        createTestRun appId2 Pending
        createTestRun appId2 Failure
    ]

    let runRepository = MockRunRepository(runs) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunsByAppIdAndStatus(appId1.Value.ToString(), "pending", 0, 10)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Ok(RunsResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)

        Assert.All(
            pagedResult.Items,
            fun item ->
                Assert.Equal(appId1, item.AppId)
                Assert.Equal(Pending, item.Status)
        )
    | Error err -> Assert.Fail($"Expected RunsResult but got error: {err}")
}

[<Fact>]
let ``GetRunsByAppIdAndStatus with invalid app ID returns validation error`` () = task {
    // Arrange
    let runRepository = MockRunRepository([]) :> IRunRepository
    let appRepository = MockAppRepository([]) :> IAppRepository
    let resourceRepository = MockResourceRepository([]) :> IResourceRepository

    let command = GetRunsByAppIdAndStatus("not-a-valid-guid", "pending", 0, 10)

    // Act
    let! result = RunHandler.handleCommand runRepository appRepository resourceRepository sqlExecutionService command

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("Invalid app ID format", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for invalid app ID")
    | Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
}