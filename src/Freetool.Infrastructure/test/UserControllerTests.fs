module Freetool.Infrastructure.Tests.UserControllerTests

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
type MockAuthorizationService(permissions: Map<AuthSubject * AuthRelation * AuthObject, bool>) =
    interface IAuthorizationService with
        member _.CreateStoreAsync(_) =
            Task.FromResult({ Id = "store-1"; Name = "test-store" })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())

        member _.CreateRelationshipsAsync(_) = Task.FromResult(())
        member _.UpdateRelationshipsAsync(_) = Task.FromResult(())
        member _.DeleteRelationshipsAsync(_) = Task.FromResult(())

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (object: AuthObject) =
            let key = (subject, relation, object)
            Task.FromResult(permissions.TryFind(key) |> Option.defaultValue false)

        member _.StoreExistsAsync(_storeId: string) = Task.FromResult(true)

        member _.BatchCheckPermissionsAsync (subject: AuthSubject) (relations: AuthRelation list) (object: AuthObject) =
            let results =
                relations
                |> List.map (fun relation ->
                    let key = (subject, relation, object)
                    (relation, permissions.TryFind(key) |> Option.defaultValue false))
                |> Map.ofList

            Task.FromResult(results)

// Mock command handler for testing - returns NotFound for all commands
type MockUserCommandHandler() =
    interface ICommandHandler<UserCommand, UserCommandResult> with
        member _.HandleCommand(command: UserCommand) =
            Task.FromResult(Error(NotFound "Mock: Command not found in test setup"))

// Helper to create a test controller with mocked dependencies
let createTestController (permissions: Map<AuthSubject * AuthRelation * AuthObject, bool>) (userId: UserId option) =
    let authService = MockAuthorizationService(permissions) :> IAuthorizationService

    let commandHandler =
        MockUserCommandHandler() :> ICommandHandler<UserCommand, UserCommandResult>

    let spaceRepository = Unchecked.defaultof<ISpaceRepository> // Not used in these tests

    let controller = UserController(spaceRepository, commandHandler, authService)

    // Setup HttpContext with a fake UserId
    let httpContext = DefaultHttpContext()
    let actualUserId = userId |> Option.defaultWith UserId.NewId
    httpContext.Items.["UserId"] <- actualUserId
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    (controller, actualUserId)

[<Fact>]
let ``UpdateUserName returns 403 when user is not self and not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId() // Different user

    // No org admin permission
    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    let updateDto: UpdateUserNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateUserName(targetUserId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``UpdateUserName succeeds when user updates their own profile`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // User is updating themselves - no special permissions needed
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    let updateDto: UpdateUserNameDto = { Name = "Updated Name" }

    // Act
    let! result = controller.UpdateUserName(userId.Value.ToString(), updateDto)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> () // Command handler returns error, but no 403 from auth check
}

[<Fact>]
let ``UpdateUserName succeeds when user is org admin updating another user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    // Grant org admin permission
    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), true)
        ]

    let controller, _ = createTestController permissions (Some userId)

    let updateDto: UpdateUserNameDto = { Name = "New Name" }

    // Act
    let! result = controller.UpdateUserName(targetUserId.Value.ToString(), updateDto)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``UpdateUserEmail returns 403 when user is not self and not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    let updateDto: UpdateUserEmailDto = { Email = "new@example.com" }

    // Act
    let! result = controller.UpdateUserEmail(targetUserId.Value.ToString(), updateDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``UpdateUserEmail succeeds when user updates their own email`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    let updateDto: UpdateUserEmailDto = { Email = "new@example.com" }

    // Act
    let! result = controller.UpdateUserEmail(userId.Value.ToString(), updateDto)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``SetProfilePicture returns 403 when user is not self and not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    let setDto = {
        ProfilePicUrl = "https://example.com/pic.jpg"
    }

    // Act
    let! result = controller.SetProfilePicture(targetUserId.Value.ToString(), setDto)

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``SetProfilePicture succeeds when user updates their own profile picture`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    let setDto = {
        ProfilePicUrl = "https://example.com/pic.jpg"
    }

    // Act
    let! result = controller.SetProfilePicture(userId.Value.ToString(), setDto)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``RemoveProfilePicture returns 403 when user is not self and not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.RemoveProfilePicture(targetUserId.Value.ToString())

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``RemoveProfilePicture succeeds when user removes their own profile picture`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.RemoveProfilePicture(userId.Value.ToString())

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``DeleteUser returns 403 when user is not org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    // No org admin permission
    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.DeleteUser(targetUserId.Value.ToString())

    // Assert
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``DeleteUser succeeds when user is org admin`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    // Grant org admin permission
    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), true)
        ]

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.DeleteUser(targetUserId.Value.ToString())

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``DeleteUser returns 403 even when user tries to delete themselves`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // No org admin permission - even self-delete requires admin
    let permissions =
        Map.ofList [
            ((User(userId.Value.ToString()), OrganizationAdmin, OrganizationObject "default"), false)
        ]

    let controller, _ = createTestController permissions (Some userId)

    // Act - User trying to delete themselves
    let! result = controller.DeleteUser(userId.Value.ToString())

    // Assert - Should still be 403 (only org admins can delete users)
    match result with
    | :? ObjectResult as objResult -> Assert.Equal(403, objResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult with status code 403")
}

[<Fact>]
let ``GetUserById allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let targetUserId = UserId.NewId()

    // No special permissions needed for read operations
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.GetUserById(targetUserId.Value.ToString())

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``GetUserByEmail allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let email = "test@example.com"

    // No special permissions needed for read operations
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.GetUserByEmail(email)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}

[<Fact>]
let ``GetUsers allows any authenticated user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()

    // No special permissions needed for read operations
    let permissions = Map.empty<AuthSubject * AuthRelation * AuthObject, bool>

    let controller, _ = createTestController permissions (Some userId)

    // Act
    let! result = controller.GetUsers(0, 10)

    // Assert - Should not be 403
    match result with
    | :? ObjectResult as objResult when objResult.StatusCode.HasValue ->
        Assert.NotEqual(403, objResult.StatusCode.Value)
    | _ -> ()
}