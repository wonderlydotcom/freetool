module Freetool.Infrastructure.Tests.SpaceDefaultMemberPermissionsControllerTests

open System
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging.Abstractions
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces
open Freetool.Api.Controllers

// ============================================================================
// Mock Types
// ============================================================================

type MockAuthorizationService(checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool) =
    interface IAuthorizationService with
        member _.CreateStoreAsync req =
            Task.FromResult({ Id = "store-1"; Name = req.Name })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())
        member _.CreateRelationshipsAsync _ = Task.FromResult(())
        member _.UpdateRelationshipsAsync _ = Task.FromResult(())
        member _.DeleteRelationshipsAsync _ = Task.FromResult(())

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (``object``: AuthObject) =
            Task.FromResult(checkPermissionFn subject relation ``object``)

        member _.StoreExistsAsync _ = Task.FromResult(true)

        member _.BatchCheckPermissionsAsync
            (subject: AuthSubject)
            (relations: AuthRelation list)
            (``object``: AuthObject)
            =
            let results =
                relations
                |> List.map (fun relation -> (relation, checkPermissionFn subject relation ``object``))
                |> Map.ofList

            Task.FromResult(results)

type CapturingSpaceCommandHandler(handleCommandFn: SpaceCommand -> Task<Result<SpaceCommandResult, DomainError>>) =
    let mutable capturedCommand: SpaceCommand option = None

    member _.CapturedCommand = capturedCommand

    interface ICommandHandler<SpaceCommand, SpaceCommandResult> with
        member _.HandleCommand(command: SpaceCommand) =
            capturedCommand <- Some command
            handleCommandFn command

// ============================================================================
// Helpers
// ============================================================================

let defaultPermissions: SpacePermissionsDto = {
    CreateResource = true
    EditResource = false
    DeleteResource = false
    CreateApp = true
    EditApp = true
    DeleteApp = false
    RunApp = true
    CreateDashboard = true
    EditDashboard = true
    DeleteDashboard = false
    RunDashboard = true
    CreateFolder = true
    EditFolder = false
    DeleteFolder = false
}

let createTestController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (handleCommandFn: SpaceCommand -> Task<Result<SpaceCommandResult, DomainError>>)
    (userId: UserId)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let handler = CapturingSpaceCommandHandler(handleCommandFn)

    let controller =
        SpaceController(
            handler :> ICommandHandler<SpaceCommand, SpaceCommandResult>,
            authService,
            NullLogger<SpaceController>.Instance
        )

    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- userId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    (controller, handler)

let createPermissionsResponse (spaceId: string) : SpaceDefaultMemberPermissionsResponseDto = {
    SpaceId = spaceId
    SpaceName = "Engineering"
    Permissions = defaultPermissions
}

// ============================================================================
// Tests
// ============================================================================

[<Fact>]
let ``GetDefaultMemberPermissions returns 403 when user is neither org-admin nor moderator`` () : Task = task {
    let userId = UserId.NewId()
    let spaceId = Guid.NewGuid().ToString()

    let checkPermission _ _ _ = false

    let controller, handler =
        createTestController
            checkPermission
            (fun _ -> Task.FromResult(Error(InvalidOperation "Should not be called")))
            userId

    let! result = controller.GetDefaultMemberPermissions(spaceId)

    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")

    match handler.CapturedCommand with
    | Some(GetSpaceById sid) -> Assert.Equal(spaceId, sid)
    | _ -> Assert.True(false, "Expected GetSpaceById fallback check")
}

[<Fact>]
let ``GetDefaultMemberPermissions returns 200 and response when user is moderator`` () : Task = task {
    let userId = UserId.NewId()
    let spaceId = Guid.NewGuid().ToString()
    let expected = createPermissionsResponse spaceId

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let controller, handler =
        createTestController
            checkPermission
            (fun cmd ->
                match cmd with
                | GetDefaultMemberPermissions sid ->
                    Assert.Equal(spaceId, sid)
                    Task.FromResult(Ok(SpaceDefaultMemberPermissionsResult expected))
                | _ -> Task.FromResult(Error(InvalidOperation "Unexpected command")))
            userId

    let! result = controller.GetDefaultMemberPermissions(spaceId)

    match result with
    | :? OkObjectResult as ok ->
        let payload = Assert.IsType<SpaceDefaultMemberPermissionsResponseDto>(ok.Value)
        Assert.Equal(expected.SpaceId, payload.SpaceId)
        Assert.Equal(expected.SpaceName, payload.SpaceName)
        Assert.Equal(expected.Permissions.RunApp, payload.Permissions.RunApp)
    | _ -> Assert.True(false, "Expected OkObjectResult")

    match handler.CapturedCommand with
    | Some(GetDefaultMemberPermissions sid) -> Assert.Equal(spaceId, sid)
    | _ -> Assert.True(false, "Expected GetDefaultMemberPermissions command")
}

[<Fact>]
let ``GetDefaultMemberPermissions falls back to persisted moderator when OpenFGA tuple is missing`` () : Task = task {
    let userId = UserId.NewId()
    let spaceGuid = Guid.NewGuid()
    let spaceId = spaceGuid.ToString()
    let expected = createPermissionsResponse spaceId

    let persistedSpace: SpaceData = {
        Id = SpaceId.FromGuid spaceGuid
        Name = "Engineering"
        ModeratorUserId = userId
        MemberIds = []
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    let checkPermission _ _ _ = false

    let controller, _ =
        createTestController
            checkPermission
            (fun cmd ->
                match cmd with
                | GetSpaceById sid when sid = spaceId -> Task.FromResult(Ok(SpaceResult persistedSpace))
                | GetDefaultMemberPermissions sid when sid = spaceId ->
                    Task.FromResult(Ok(SpaceDefaultMemberPermissionsResult expected))
                | _ -> Task.FromResult(Error(InvalidOperation "Unexpected command")))
            userId

    let! result = controller.GetDefaultMemberPermissions(spaceId)

    match result with
    | :? OkObjectResult as ok ->
        let payload = Assert.IsType<SpaceDefaultMemberPermissionsResponseDto>(ok.Value)
        Assert.Equal(expected.SpaceId, payload.SpaceId)
        Assert.Equal(expected.Permissions.CreateApp, payload.Permissions.CreateApp)
    | _ -> Assert.True(false, "Expected OkObjectResult")
}

[<Fact>]
let ``UpdateDefaultMemberPermissions returns 403 when user is neither org-admin nor moderator`` () : Task = task {
    let userId = UserId.NewId()
    let spaceId = Guid.NewGuid().ToString()

    let checkPermission _ _ _ = false

    let controller, handler =
        createTestController
            checkPermission
            (fun _ -> Task.FromResult(Error(InvalidOperation "Should not be called")))
            userId

    let dto: UpdateDefaultMemberPermissionsDto = { Permissions = defaultPermissions }

    let! result = controller.UpdateDefaultMemberPermissions(spaceId, dto)

    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")

    match handler.CapturedCommand with
    | Some(GetSpaceById sid) -> Assert.Equal(spaceId, sid)
    | _ -> Assert.True(false, "Expected GetSpaceById fallback check")
}

[<Fact>]
let ``UpdateDefaultMemberPermissions returns 200 and emits command when user is org-admin`` () : Task = task {
    let userId = UserId.NewId()
    let spaceId = Guid.NewGuid().ToString()

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let controller, handler =
        createTestController
            checkPermission
            (fun cmd ->
                match cmd with
                | UpdateDefaultMemberPermissions(actorUserId, sid, payload) ->
                    Assert.Equal(userId, actorUserId)
                    Assert.Equal(spaceId, sid)
                    Assert.Equal(defaultPermissions.CreateApp, payload.Permissions.CreateApp)
                    Task.FromResult(Ok(SpaceCommandResult.UnitResult()))
                | _ -> Task.FromResult(Error(InvalidOperation "Unexpected command")))
            userId

    let dto: UpdateDefaultMemberPermissionsDto = { Permissions = defaultPermissions }

    let! result = controller.UpdateDefaultMemberPermissions(spaceId, dto)

    match result with
    | :? OkResult -> Assert.True(true)
    | :? StatusCodeResult as status -> Assert.Equal(200, status.StatusCode)
    | _ -> Assert.True(false, "Expected OkResult")

    match handler.CapturedCommand with
    | Some(UpdateDefaultMemberPermissions(actorUserId, sid, _)) ->
        Assert.Equal(userId, actorUserId)
        Assert.Equal(spaceId, sid)
    | _ -> Assert.True(false, "Expected UpdateDefaultMemberPermissions command")
}