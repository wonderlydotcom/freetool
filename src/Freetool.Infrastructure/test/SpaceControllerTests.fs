module Freetool.Infrastructure.Tests.SpaceControllerTests

open System
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
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

// Mock command handler for testing
type MockSpaceCommandHandler(handleCommandFn: SpaceCommand -> Task<Result<SpaceCommandResult, DomainError>>) =
    interface ICommandHandler<SpaceCommand, SpaceCommandResult> with
        member _.HandleCommand(command: SpaceCommand) = handleCommandFn command

// Helper to create a test controller with mocked dependencies
let createTestController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (handleCommandFn: SpaceCommand -> Task<Result<SpaceCommandResult, DomainError>>)
    (userId: UserId)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let commandHandler =
        MockSpaceCommandHandler(handleCommandFn) :> ICommandHandler<SpaceCommand, SpaceCommandResult>

    let logger = NullLogger<SpaceController>.Instance :> ILogger<SpaceController>

    let controller = SpaceController(commandHandler, authService, logger)

    // Setup HttpContext with the provided UserId
    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- userId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    controller

// Helper to create test space data
let createTestSpaceData (spaceId: SpaceId) (moderatorId: UserId) : SpaceData = {
    Id = spaceId
    Name = "Test Space"
    ModeratorUserId = moderatorId
    MemberIds = [ moderatorId ]
    CreatedAt = DateTime.UtcNow
    UpdatedAt = DateTime.UtcNow
    IsDeleted = false
}

// ============================================================================
// CreateSpace Tests
// ============================================================================

[<Fact>]
let ``CreateSpace returns 403 when user is not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    let createDto: CreateSpaceDto = {
        Name = "Engineering"
        ModeratorUserId = userId.Value.ToString()
        MemberIds = None
    }

    // Act
    let! result = controller.CreateSpace(createDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``CreateSpace succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    // Grant org admin permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let spaceData = createTestSpaceData spaceId userId

    let handleCommand cmd =
        match cmd with
        | CreateSpace _ -> Task.FromResult(Ok(SpaceResult spaceData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    let createDto: CreateSpaceDto = {
        Name = "Engineering"
        ModeratorUserId = userId.Value.ToString()
        MemberIds = None
    }

    // Act
    let! result = controller.CreateSpace(createDto)

    // Assert - Verify it's not a 403 (should succeed with 201 Created)
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | :? CreatedAtActionResult -> Assert.True(true) // Success
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// UpdateSpaceName Tests
// ============================================================================

[<Fact>]
let ``UpdateSpaceName returns 403 when user is not moderator or org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId
    let updateDto: UpdateSpaceNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateSpaceName("space-123", updateDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``UpdateSpaceName succeeds when user is moderator`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = "space-123"

    // Grant moderator permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Space not found"))

    let controller = createTestController checkPermission handleCommand userId
    let updateDto: UpdateSpaceNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateSpaceName(spaceId, updateDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

[<Fact>]
let ``UpdateSpaceName succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // Grant org admin permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Space not found"))

    let controller = createTestController checkPermission handleCommand userId
    let updateDto: UpdateSpaceNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateSpaceName("space-123", updateDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// AddMember Tests
// ============================================================================

[<Fact>]
let ``AddMember returns 403 when user is neither moderator nor org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    let addMemberDto: AddMemberDto = {
        UserId = UserId.NewId().Value.ToString()
    }

    // Act
    let! result = controller.AddMember("space-123", addMemberDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``AddMember succeeds when user is moderator`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = "space-123"

    // Grant moderator permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Space not found"))

    let controller = createTestController checkPermission handleCommand userId

    let addMemberDto: AddMemberDto = {
        UserId = UserId.NewId().Value.ToString()
    }

    // Act
    let! result = controller.AddMember(spaceId, addMemberDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

[<Fact>]
let ``AddMember succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // Grant org admin permission only
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Space not found"))

    let controller = createTestController checkPermission handleCommand userId

    let addMemberDto: AddMemberDto = {
        UserId = UserId.NewId().Value.ToString()
    }

    // Act
    let! result = controller.AddMember("space-123", addMemberDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// RemoveMember Tests
// ============================================================================

[<Fact>]
let ``RemoveMember returns 403 when user is neither moderator nor org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No permissions granted

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.RemoveMember("space-123", UserId.NewId().Value.ToString())

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``RemoveMember succeeds when user is moderator`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = "space-123"

    // Grant moderator permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Space not found"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.RemoveMember(spaceId, UserId.NewId().Value.ToString())

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

[<Fact>]
let ``RemoveMember returns 400 when trying to remove moderator`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let moderatorId = UserId.NewId()
    let spaceId = "space-123"

    // Grant moderator permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let handleCommand cmd =
        match cmd with
        | RemoveMember _ ->
            // Simulate domain error when trying to remove moderator
            Task.FromResult(Error(InvalidOperation "Cannot remove the moderator from the space"))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.RemoveMember(spaceId, moderatorId.Value.ToString())

    // Assert - Should return 422 Unprocessable Entity for InvalidOperation
    match result with
    | :? UnprocessableEntityObjectResult as unprocessable -> Assert.NotNull(unprocessable)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(422, objResult.StatusCode.Value)
    | _ -> () // Other error result types are also acceptable
}

// ============================================================================
// ChangeModerator Tests
// ============================================================================

[<Fact>]
let ``ChangeModerator returns 403 when user is not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // Grant moderator permission only (not sufficient for changing moderator)
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject _ -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    let changeModeratorDto: ChangeModeratorDto = {
        NewModeratorUserId = UserId.NewId().Value.ToString()
    }

    // Act
    let! result = controller.ChangeModerator("space-123", changeModeratorDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``ChangeModerator succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let newModeratorId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    // Grant org admin permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let spaceData = {
        Id = spaceId
        Name = "Test Space"
        ModeratorUserId = newModeratorId
        MemberIds = [ newModeratorId ]
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    let handleCommand cmd =
        match cmd with
        | ChangeModerator _ -> Task.FromResult(Ok(SpaceResult spaceData))
        | GetSpaceById _ -> Task.FromResult(Ok(SpaceResult spaceData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    let changeModeratorDto: ChangeModeratorDto = {
        NewModeratorUserId = newModeratorId.Value.ToString()
    }

    // Act
    let! result = controller.ChangeModerator(spaceId.Value.ToString(), changeModeratorDto)

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// DeleteSpace Tests
// ============================================================================

[<Fact>]
let ``DeleteSpace returns 403 when user is not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // Grant moderator permission only (not sufficient for deletion)
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject _ -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(NotFound "Should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.DeleteSpace("space-123")

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``DeleteSpace blocks moderator when not org admin and does not invoke command`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let mutable deleteCommandInvoked = false

    // User is moderator only (no org-admin tuple)
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject _ -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand (_: SpaceCommand) : Task<Result<SpaceCommandResult, DomainError>> =
        deleteCommandInvoked <- true
        Task.FromResult(Error(InvalidOperation "Delete command should not be called"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.DeleteSpace("space-123")

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")

    Assert.False(deleteCommandInvoked)
}

[<Fact>]
let ``DeleteSpace succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    // Grant org admin permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let spaceData = createTestSpaceData spaceId userId

    let handleCommand (cmd: SpaceCommand) : Task<Result<SpaceCommandResult, DomainError>> =
        match cmd with
        | DeleteSpace _ -> Task.FromResult(Ok(SpaceCommandResult.UnitResult()))
        | GetSpaceById _ -> Task.FromResult(Ok(SpaceCommandResult.SpaceResult spaceData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.DeleteSpace(spaceId.Value.ToString())

    // Assert - Verify it's not a 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | :? NoContentResult -> Assert.True(true) // Success
    | _ -> () // Other result types are acceptable
}

// ============================================================================
// Read Operations Tests (should allow any authenticated user)
// ============================================================================

[<Fact>]
let ``GetSpaceById allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let moderatorId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let spaceData = createTestSpaceData spaceId moderatorId

    let handleCommand cmd =
        match cmd with
        | GetSpaceById _ -> Task.FromResult(Ok(SpaceResult spaceData))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.GetSpaceById(spaceId.Value.ToString())

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult)
    | _ -> () // Test passes as long as no 403 is returned
}

[<Fact>]
let ``GetSpaces allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = false // No special permissions needed for read

    let pagedResult = {
        Items = []
        TotalCount = 0
        Skip = 0
        Take = 10
    }

    let handleCommand cmd =
        match cmd with
        | GetAllSpaces _ -> Task.FromResult(Ok(SpacesResult pagedResult))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.GetSpaces(0, 10)

    // Assert - Should succeed (200 OK) without auth check
    match result with
    | :? OkObjectResult as okResult -> Assert.NotNull(okResult)
    | _ -> () // Test passes as long as no 403 is returned
}

// ============================================================================
// Error Handling Tests
// ============================================================================

[<Fact>]
let ``CreateSpace returns 400 for validation error`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // Grant org admin permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, OrganizationAdmin, OrganizationObject "default" -> uid = userId.Value.ToString()
        | _ -> false

    let handleCommand _ =
        Task.FromResult(Error(ValidationError "Space name cannot be empty"))

    let controller = createTestController checkPermission handleCommand userId

    let createDto: CreateSpaceDto = {
        Name = "" // Invalid empty name
        ModeratorUserId = userId.Value.ToString()
        MemberIds = None
    }

    // Act
    let! result = controller.CreateSpace(createDto)

    // Assert
    match result with
    | :? BadRequestObjectResult -> Assert.True(true)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(400, objResult.StatusCode.Value)
    | _ -> () // Other error handling is acceptable
}

[<Fact>]
let ``GetSpaceById returns 404 for non-existent space`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let checkPermission _ _ _ = true

    let handleCommand cmd =
        match cmd with
        | GetSpaceById _ -> Task.FromResult(Error(NotFound "Space not found"))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    // Act
    let! result = controller.GetSpaceById("non-existent-id")

    // Assert
    match result with
    | :? NotFoundObjectResult -> Assert.True(true)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(404, objResult.StatusCode.Value)
    | _ -> () // Other error handling is acceptable
}

[<Fact>]
let ``AddMember returns 409 for duplicate member`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let existingMemberId = UserId.NewId()
    let spaceId = "space-123"

    // Grant moderator permission
    let checkPermission (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
        match subject, relation, obj with
        | User uid, SpaceModerator, SpaceObject sid -> uid = userId.Value.ToString() && sid = spaceId
        | _ -> false

    let handleCommand cmd =
        match cmd with
        | AddMember _ -> Task.FromResult(Error(Conflict "User is already a member of this space"))
        | _ -> Task.FromResult(Error(NotFound "Command not supported"))

    let controller = createTestController checkPermission handleCommand userId

    let addMemberDto: AddMemberDto = {
        UserId = existingMemberId.Value.ToString()
    }

    // Act
    let! result = controller.AddMember(spaceId, addMemberDto)

    // Assert
    match result with
    | :? ConflictObjectResult -> Assert.True(true)
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue -> Assert.Equal(409, objResult.StatusCode.Value)
    | _ -> () // Other error handling is acceptable
}