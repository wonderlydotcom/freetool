module Freetool.Infrastructure.Tests.IdentityProvisioningControllerTests

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

type MockIdentityGroupSpaceMappingRepository
    (
        getAllFn: unit -> Task<IdentityGroupSpaceMappingDto list>,
        addFn: UserId -> string -> SpaceId -> Task<Result<IdentityGroupSpaceMappingDto, DomainError>>,
        updateFn: UserId -> Guid -> bool -> Task<Result<unit, DomainError>>,
        deleteFn: Guid -> Task<Result<unit, DomainError>>
    ) =
    interface IIdentityGroupSpaceMappingRepository with
        member _.GetAllAsync() = getAllFn ()
        member _.GetSpaceIdsByGroupKeysAsync _ = Task.FromResult([])
        member _.AddAsync (actorUserId) (groupKey) (spaceId) = addFn actorUserId groupKey spaceId
        member _.UpdateIsActiveAsync (actorUserId) (mappingId) (isActive) = updateFn actorUserId mappingId isActive
        member _.DeleteAsync(mappingId) = deleteFn mappingId

// ============================================================================
// Helpers
// ============================================================================

let createTestController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (getAllFn: unit -> Task<IdentityGroupSpaceMappingDto list>)
    (addFn: UserId -> string -> SpaceId -> Task<Result<IdentityGroupSpaceMappingDto, DomainError>>)
    (updateFn: UserId -> Guid -> bool -> Task<Result<unit, DomainError>>)
    (deleteFn: Guid -> Task<Result<unit, DomainError>>)
    (userId: UserId)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let mappingRepository =
        MockIdentityGroupSpaceMappingRepository(getAllFn, addFn, updateFn, deleteFn)
        :> IIdentityGroupSpaceMappingRepository

    let controller =
        IdentityProvisioningController(
            mappingRepository,
            authService,
            NullLogger<IdentityProvisioningController>.Instance
        )

    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- userId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    controller

let createMappingDto (id: Guid) (spaceId: SpaceId) = {
    Id = id.ToString()
    GroupKey = "eng"
    SpaceId = spaceId.Value.ToString()
    SpaceName = Some "Engineering"
    IsActive = true
    CreatedAt = DateTime.UtcNow
    UpdatedAt = DateTime.UtcNow
}

// ============================================================================
// Tests
// ============================================================================

[<Fact>]
let ``GetMappings returns 403 for non-org-admin`` () : Task = task {
    let userId = UserId.NewId()

    let checkPermission _ _ _ = false

    let controller =
        createTestController
            checkPermission
            (fun () -> Task.FromResult([]))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            userId

    let! result = controller.GetMappings()

    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``CreateMapping returns 400 for invalid space ID format`` () : Task = task {
    let userId = UserId.NewId()

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let controller =
        createTestController
            checkPermission
            (fun () -> Task.FromResult([]))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Should not be called")))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            userId

    let dto: CreateIdentityGroupSpaceMappingDto = {
        GroupKey = "eng"
        SpaceId = "not-a-guid"
    }

    let! result = controller.CreateMapping(dto)

    match result with
    | :? BadRequestObjectResult -> Assert.True(true)
    | :? ObjectResult as objResult -> Assert.Equal(400, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected bad request")
}

[<Fact>]
let ``CreateMapping returns 201 and mapping payload for org-admin`` () : Task = task {
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let mappingId = Guid.NewGuid()
    let expected = createMappingDto mappingId spaceId

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let controller =
        createTestController
            checkPermission
            (fun () -> Task.FromResult([]))
            (fun actor groupKey targetSpaceId ->
                Assert.Equal(userId, actor)
                Assert.Equal("eng", groupKey)
                Assert.Equal(spaceId, targetSpaceId)
                Task.FromResult(Ok expected))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            userId

    let dto: CreateIdentityGroupSpaceMappingDto = {
        GroupKey = "eng"
        SpaceId = spaceId.Value.ToString()
    }

    let! result = controller.CreateMapping(dto)

    match result with
    | :? ObjectResult as objResult ->
        Assert.Equal(201, objResult.StatusCode.Value)
        let payload = Assert.IsType<IdentityGroupSpaceMappingDto>(objResult.Value)
        Assert.Equal(expected.Id, payload.Id)
        Assert.Equal(expected.GroupKey, payload.GroupKey)
        Assert.Equal(expected.SpaceId, payload.SpaceId)
    | _ -> Assert.True(false, "Expected 201 ObjectResult")
}

[<Fact>]
let ``UpdateMapping returns 400 for invalid mapping ID format`` () : Task = task {
    let userId = UserId.NewId()

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let controller =
        createTestController
            checkPermission
            (fun () -> Task.FromResult([]))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Should not be called")))
            (fun _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            userId

    let dto: UpdateIdentityGroupSpaceMappingDto = { IsActive = false }
    let! result = controller.UpdateMapping("not-a-guid", dto)

    match result with
    | :? BadRequestObjectResult -> Assert.True(true)
    | :? ObjectResult as objResult -> Assert.Equal(400, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected bad request")
}

[<Fact>]
let ``DeleteMapping returns 204 for org-admin`` () : Task = task {
    let userId = UserId.NewId()
    let mappingId = Guid.NewGuid()

    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let controller =
        createTestController
            checkPermission
            (fun () -> Task.FromResult([]))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun _ _ _ -> Task.FromResult(Error(InvalidOperation "Not used")))
            (fun id ->
                Assert.Equal(mappingId, id)
                Task.FromResult(Ok()))
            userId

    let! result = controller.DeleteMapping(mappingId.ToString())

    match result with
    | :? NoContentResult -> Assert.True(true)
    | :? StatusCodeResult as status -> Assert.Equal(204, status.StatusCode)
    | _ -> Assert.True(false, "Expected NoContentResult")
}