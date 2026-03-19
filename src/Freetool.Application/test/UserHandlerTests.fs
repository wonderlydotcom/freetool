module Freetool.Application.Tests.UserHandlerTests

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
type MockUserRepository(users: ValidatedUser list) =
    let mutable userList = users
    let mutable emailConflicts = Map.empty<string, bool>

    member _.SetEmailConflict(email: string, hasConflict: bool) =
        emailConflicts <- emailConflicts.Add(email.ToLower(), hasConflict)

    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) : Task<ValidatedUser option> = task {
            return userList |> List.tryFind (fun u -> u.State.Id = userId)
        }

        member _.GetByEmailAsync(email: Email) : Task<ValidatedUser option> = task {
            return
                userList
                |> List.tryFind (fun u -> u.State.Email.ToLower() = email.Value.ToLower())
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedUser list> = task {
            return userList |> List.skip skip |> List.truncate take
        }

        member _.AddAsync(user: ValidatedUser) : Task<Result<unit, DomainError>> = task {
            userList <- user :: userList
            return Ok()
        }

        member _.UpdateAsync(user: ValidatedUser) : Task<Result<unit, DomainError>> = task {
            userList <- user :: (userList |> List.filter (fun u -> u.State.Id <> user.State.Id))
            return Ok()
        }

        member _.DeleteAsync(user: ValidatedUser) : Task<Result<unit, DomainError>> = task {
            userList <- userList |> List.filter (fun u -> u.State.Id <> user.State.Id)
            return Ok()
        }

        member _.ExistsAsync(userId: UserId) : Task<bool> = task {
            return userList |> List.exists (fun u -> u.State.Id = userId)
        }

        member _.ExistsByEmailAsync(email: Email) : Task<bool> = task {
            let key = email.Value.ToLower()

            match emailConflicts.TryFind key with
            | Some value -> return value
            | None ->
                return
                    userList
                    |> List.exists (fun u -> u.State.Email.ToLower() = email.Value.ToLower())
        }

        member _.GetCountAsync() : Task<int> = task { return userList.Length }

// Test helper to create a user
let createTestUser (name: string) (email: string) : ValidatedUser =
    match Email.Create(Some email) with
    | Ok validEmail -> User.create name validEmail None
    | Error _ -> failwith "Failed to create test user - invalid email"

// Test helper to create a user with profile picture
let createTestUserWithProfilePic (name: string) (email: string) (profilePicUrl: string) : ValidatedUser =
    match Email.Create(Some email) with
    | Ok validEmail -> User.create name validEmail (Some profilePicUrl)
    | Error _ -> failwith "Failed to create test user - invalid email"

[<Fact>]
let ``CreateUser succeeds with valid user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let newUser = createTestUser "John Doe" "john@example.com"

    let repository = MockUserRepository([]) :> IUserRepository
    let command = CreateUser(actorUserId, newUser)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) ->
        Assert.Equal("John Doe", userData.Name)
        Assert.Equal("john@example.com", userData.Email)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``CreateUser fails when email exists`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "Jane Doe" "jane@example.com"
    let newUser = createTestUser "John Doe" "jane@example.com"

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let command = CreateUser(actorUserId, newUser)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already exists", msg)
    | Ok _ -> Assert.Fail("Expected Conflict error for duplicate email")
    | Error err -> Assert.Fail($"Expected Conflict error but got: {err}")
}

[<Fact>]
let ``DeleteUser removes user from repository`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "User to Delete" "delete@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let command = DeleteUser(actorUserId, userId.Value.ToString())

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UnitResult()) -> Assert.True(true)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UnitResult")
}

[<Fact>]
let ``DeleteUser returns NotFound for nonexistent user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let nonExistentUserId = Guid.NewGuid().ToString()

    let repository = MockUserRepository([]) :> IUserRepository
    let command = DeleteUser(actorUserId, nonExistentUserId)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Error(NotFound _) -> Assert.True(true)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``UpdateUserName succeeds for existing user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "Original Name" "user@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let dto: UpdateUserNameDto = { Name = "Updated Name" }
    let command = UpdateUserName(actorUserId, userId.Value.ToString(), dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) -> Assert.Equal("Updated Name", userData.Name)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``UpdateUserName returns NotFound for nonexistent user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let nonExistentUserId = Guid.NewGuid().ToString()

    let repository = MockUserRepository([]) :> IUserRepository
    let dto: UpdateUserNameDto = { Name = "Updated Name" }
    let command = UpdateUserName(actorUserId, nonExistentUserId, dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Error(NotFound _) -> Assert.True(true)
    | Ok _ -> Assert.Fail("Expected NotFound error")
    | Error err -> Assert.Fail($"Expected NotFound error but got: {err}")
}

[<Fact>]
let ``UpdateUserEmail succeeds for existing user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "Test User" "old@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let dto: UpdateUserEmailDto = { Email = "new@example.com" }
    let command = UpdateUserEmail(actorUserId, userId.Value.ToString(), dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) -> Assert.Equal("new@example.com", userData.Email)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``UpdateUserEmail returns ValidationError for invalid email`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "Test User" "valid@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let dto: UpdateUserEmailDto = { Email = "invalid-email" }
    let command = UpdateUserEmail(actorUserId, userId.Value.ToString(), dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError _) -> Assert.True(true)
    | Ok _ -> Assert.Fail("Expected ValidationError for invalid email")
    | Error err -> Assert.Fail($"Expected ValidationError but got: {err}")
}

[<Fact>]
let ``SetProfilePicture updates user profile pic`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()
    let existingUser = createTestUser "Test User" "user@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository

    let dto: SetProfilePictureDto = {
        ProfilePicUrl = "https://example.com/profile.jpg"
    }

    let command = SetProfilePicture(actorUserId, userId.Value.ToString(), dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) ->
        Assert.True(userData.ProfilePicUrl.IsSome)
        Assert.Equal("https://example.com/profile.jpg", userData.ProfilePicUrl.Value)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``RemoveProfilePicture clears profile pic`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()

    let existingUser =
        createTestUserWithProfilePic "Test User" "user@example.com" "https://example.com/old.jpg"

    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let command = RemoveProfilePicture(actorUserId, userId.Value.ToString())

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) -> Assert.True(userData.ProfilePicUrl.IsNone)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``GetUserById returns user when exists`` () = task {
    // Arrange
    let existingUser = createTestUser "Test User" "user@example.com"
    let userId = User.getId existingUser

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let command = GetUserById(userId.Value.ToString())

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) ->
        Assert.Equal("Test User", userData.Name)
        Assert.Equal(userId, userData.Id)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``GetUserByEmail returns user when exists`` () = task {
    // Arrange
    let existingUser = createTestUser "Test User" "findme@example.com"

    let repository = MockUserRepository([ existingUser ]) :> IUserRepository
    let command = GetUserByEmail("findme@example.com")

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) ->
        Assert.Equal("Test User", userData.Name)
        Assert.Equal("findme@example.com", userData.Email)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}

[<Fact>]
let ``GetAllUsers returns paginated results`` () = task {
    // Arrange
    let users = [
        createTestUser "User 1" "user1@example.com"
        createTestUser "User 2" "user2@example.com"
        createTestUser "User 3" "user3@example.com"
        createTestUser "User 4" "user4@example.com"
        createTestUser "User 5" "user5@example.com"
    ]

    let repository = MockUserRepository(users) :> IUserRepository
    let command = GetAllUsers(1, 2) // Skip 1, take 2

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UsersResult pagedResult) ->
        Assert.Equal(5, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.Equal(1, pagedResult.Skip)
        Assert.Equal(2, pagedResult.Take)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UsersResult")
}

[<Fact>]
let ``InviteUser creates placeholder user`` () = task {
    // Arrange
    let actorUserId = UserId.NewId()

    let repository = MockUserRepository([]) :> IUserRepository
    let dto: InviteUserDto = { Email = "invited@example.com" }
    let command = InviteUser(actorUserId, dto)

    // Act
    let! result = UserHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(UserResult userData) ->
        Assert.Equal("invited@example.com", userData.Email)
        Assert.Equal("", userData.Name) // Placeholder users have empty name
        Assert.True(userData.InvitedAt.IsSome)
    | Error err -> Assert.Fail($"Expected success but got error: {err}")
    | _ -> Assert.Fail("Expected UserResult")
}