module Freetool.Infrastructure.Tests.DashboardControllerIntegrationTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Xunit
open Freetool.Api.Controllers
open Freetool.Application.Commands
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects

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

        member _.StoreExistsAsync(_) = Task.FromResult(true)

        member _.BatchCheckPermissionsAsync (subject: AuthSubject) (relations: AuthRelation list) (object: AuthObject) =
            let results =
                relations
                |> List.map (fun relation -> relation, checkPermissionFn subject relation object)
                |> Map.ofList

            Task.FromResult(results)

type MockDashboardRepository(dashboard: ValidatedDashboard option) =
    interface IDashboardRepository with
        member _.GetByIdAsync(_dashboardId: DashboardId) = task { return dashboard }
        member _.GetByFolderIdAsync _ _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ _ = task { return Ok() }
        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetCountAsync() = task { return 0 }

type MockFolderRepository(folder: ValidatedFolder option) =
    interface IFolderRepository with
        member _.GetByIdAsync(_folderId: FolderId) = task { return folder }
        member _.GetChildrenAsync _ = task { return [] }
        member _.GetRootFoldersAsync _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceAsync _ _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.ExistsByNameInParentAsync _ _ = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountBySpaceAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetRootCountAsync() = task { return 0 }
        member _.GetChildCountAsync _ = task { return 0 }
        member _.GetDeletedBySpaceAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreWithChildrenAsync _ = task { return Ok 0 }
        member _.CheckNameConflictAsync _ _ _ = task { return false }

type MockSpaceRepository() =
    interface ISpaceRepository with
        member _.GetByIdAsync _ = task { return None }
        member _.GetByNameAsync _ = task { return None }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetByUserIdAsync _ = task { return [] }
        member _.GetByModeratorUserIdAsync _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.ExistsByNameAsync _ = task { return false }
        member _.GetCountAsync() = task { return 0 }

type MockUserRepository(testUser: ValidatedUser option) =
    interface IUserRepository with
        member _.GetByIdAsync _ = task { return testUser }
        member _.GetByEmailAsync _ = task { return None }
        member _.GetAllAsync _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.ExistsByEmailAsync _ = task { return false }
        member _.GetCountAsync() = task { return 0 }

type MockDashboardCommandHandler(handleCommandFn: DashboardCommand -> Task<Result<DashboardCommandResult, DomainError>>)
    =
    interface ICommandHandler<DashboardCommand, DashboardCommandResult> with
        member _.HandleCommand(command: DashboardCommand) = handleCommandFn command

let private createTestFolder (spaceId: SpaceId) (folderId: FolderId) : ValidatedFolder =
    let folderName =
        FolderName.Create(Some "Operations") |> Result.toOption |> Option.get

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

let private createTestDashboard (actorUserId: UserId) (folderId: FolderId) : ValidatedDashboard =
    match
        Dashboard.create
            actorUserId
            "Ops Dashboard"
            folderId
            None
            """{"loadInputs":[],"actionInputs":[],"actions":[],"bindings":[],"layout":{"left":[],"center":[],"right":[]}}"""
    with
    | Ok dashboard -> dashboard
    | Error error -> failwith $"Failed to create test dashboard: {error}"

let private createTestUser () : ValidatedUser =
    let email =
        Email.Create(Some "integration@test.dev") |> Result.toOption |> Option.get

    User.create "Integration User" email None

let private createController
    (dashboard: ValidatedDashboard)
    (folder: ValidatedFolder)
    (authFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (user: ValidatedUser option)
    (commandHandlerFn: DashboardCommand -> Task<Result<DashboardCommandResult, DomainError>>)
    (currentUserId: UserId)
    =
    let controller =
        DashboardController(
            MockDashboardRepository(Some dashboard),
            MockFolderRepository(Some folder),
            MockSpaceRepository(),
            MockUserRepository(user),
            MockAuthorizationService(authFn),
            MockDashboardCommandHandler(commandHandlerFn)
        )

    let httpContext = DefaultHttpContext()
    httpContext.Items["UserId"] <- currentUserId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)
    controller

[<Fact>]
let ``PrepareDashboard returns forbidden when user lacks run_app`` () : Task = task {
    let actorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let folder = createTestFolder spaceId folderId
    let dashboard = createTestDashboard actorUserId folderId
    let mutable commandInvoked = false

    let controller =
        createController
            dashboard
            folder
            (fun _ relation _ ->
                match relation with
                | DashboardRun -> true
                | AppRun -> false
                | _ -> false)
            (Some(createTestUser ()))
            (fun _ ->
                commandInvoked <- true
                task { return Ok(DashboardUnitResult()) })
            actorUserId

    let! result = controller.PrepareDashboard(dashboard.State.Id.Value.ToString(), { LoadInputs = [] })

    let forbidden = Assert.IsType<ObjectResult>(result)
    Assert.True(forbidden.StatusCode.HasValue)
    Assert.Equal(403, forbidden.StatusCode.Value)
    Assert.False(commandInvoked)
}

[<Fact>]
let ``PrepareDashboard returns success when runtime permissions are granted`` () : Task = task {
    let actorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let folder = createTestFolder spaceId folderId
    let dashboard = createTestDashboard actorUserId folderId
    let mutable capturedCommand: DashboardCommand option = None

    let controller =
        createController
            dashboard
            folder
            (fun _ relation _ ->
                match relation with
                | DashboardRun
                | AppRun -> true
                | _ -> false)
            (Some(createTestUser ()))
            (fun command ->
                capturedCommand <- Some command

                task {
                    return
                        Ok(
                            DashboardPrepareResult {
                                PrepareRunId = Guid.NewGuid().ToString()
                                Status = "Success"
                                Response = Some "{}"
                                ErrorMessage = None
                            }
                        )
                })
            actorUserId

    let! result = controller.PrepareDashboard(dashboard.State.Id.Value.ToString(), { LoadInputs = [] })

    let okResult = Assert.IsType<OkObjectResult>(result)
    let payload = Assert.IsType<DashboardPrepareResponseDto>(okResult.Value)
    Assert.Equal("Success", payload.Status)
    Assert.True(capturedCommand.IsSome)

    match capturedCommand.Value with
    | PrepareDashboard(_, dashboardId, _, _) -> Assert.Equal(dashboard.State.Id.Value.ToString(), dashboardId)
    | _ -> Assert.Fail("Expected PrepareDashboard command")
}

[<Fact>]
let ``RunDashboardAction returns bad request when actionId is invalid`` () : Task = task {
    let actorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let folder = createTestFolder spaceId folderId
    let dashboard = createTestDashboard actorUserId folderId
    let mutable commandInvoked = false

    let controller =
        createController
            dashboard
            folder
            (fun _ _ _ -> true)
            (Some(createTestUser ()))
            (fun _ ->
                commandInvoked <- true
                task { return Ok(DashboardUnitResult()) })
            actorUserId

    let! result =
        controller.RunDashboardAction(
            dashboard.State.Id.Value.ToString(),
            "",
            {
                PrepareRunId = None
                LoadInputs = []
                ActionInputs = []
                PriorActionRunIds = None
            }
        )

    Assert.IsType<BadRequestObjectResult>(result) |> ignore
    Assert.False(commandInvoked)
}

[<Fact>]
let ``RunDashboardAction returns success when runtime permissions are granted`` () : Task = task {
    let actorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let folder = createTestFolder spaceId folderId
    let dashboard = createTestDashboard actorUserId folderId
    let mutable capturedCommand: DashboardCommand option = None

    let controller =
        createController
            dashboard
            folder
            (fun _ relation _ ->
                match relation with
                | DashboardRun
                | AppRun -> true
                | _ -> false)
            (Some(createTestUser ()))
            (fun command ->
                capturedCommand <- Some command

                task {
                    return
                        Ok(
                            DashboardActionResult {
                                ActionRunId = Guid.NewGuid().ToString()
                                Status = "Success"
                                Response = Some """{"ok":true}"""
                                ErrorMessage = None
                            }
                        )
                })
            actorUserId

    let actionId = Guid.NewGuid().ToString()

    let! result =
        controller.RunDashboardAction(
            dashboard.State.Id.Value.ToString(),
            actionId,
            {
                PrepareRunId = None
                LoadInputs = []
                ActionInputs = []
                PriorActionRunIds = None
            }
        )

    let okResult = Assert.IsType<OkObjectResult>(result)
    let payload = Assert.IsType<DashboardActionResponseDto>(okResult.Value)
    Assert.Equal("Success", payload.Status)
    Assert.True(capturedCommand.IsSome)

    match capturedCommand.Value with
    | RunDashboardAction(_, dashboardId, parsedActionId, _, _) ->
        Assert.Equal(dashboard.State.Id.Value.ToString(), dashboardId)
        Assert.Equal(actionId, parsedActionId.Value)
    | _ -> Assert.Fail("Expected RunDashboardAction command")
}