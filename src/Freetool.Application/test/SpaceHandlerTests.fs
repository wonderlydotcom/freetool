module Freetool.Application.Tests.SpaceHandlerTests

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

// Mock User Repository for testing
type MockUserRepository(users: ValidatedUser list) =
    let mutable userList = users

    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) : Task<ValidatedUser option> = task {
            return userList |> List.tryFind (fun u -> u.State.Id = userId)
        }

        member _.GetByEmailAsync(email: Email) : Task<ValidatedUser option> = task {
            return userList |> List.tryFind (fun u -> u.State.Email = email.Value)
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
            return userList |> List.exists (fun u -> u.State.Email = email.Value)
        }

        member _.GetCountAsync() : Task<int> = task { return userList.Length }

// Mock Space Repository for testing
type MockSpaceRepository(spaces: ValidatedSpace list) =
    let mutable spaceList = spaces

    interface ISpaceRepository with
        member _.GetByIdAsync(spaceId: SpaceId) : Task<ValidatedSpace option> = task {
            return spaceList |> List.tryFind (fun s -> s.State.Id = spaceId)
        }

        member _.GetByNameAsync(name: string) : Task<ValidatedSpace option> = task {
            return spaceList |> List.tryFind (fun s -> s.State.Name = name)
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedSpace list> = task {
            return spaceList |> List.skip skip |> List.truncate take
        }

        member _.GetByUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaceList |> List.filter (fun s -> List.contains userId s.State.MemberIds)
        }

        member _.GetByModeratorUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaceList |> List.filter (fun s -> s.State.ModeratorUserId = userId)
        }

        member _.AddAsync(space: ValidatedSpace) : Task<Result<unit, DomainError>> = task {
            spaceList <- space :: spaceList
            return Ok()
        }

        member _.UpdateAsync(space: ValidatedSpace) : Task<Result<unit, DomainError>> = task {
            spaceList <- space :: (spaceList |> List.filter (fun s -> s.State.Id <> space.State.Id))
            return Ok()
        }

        member _.DeleteAsync(space: ValidatedSpace) : Task<Result<unit, DomainError>> = task {
            spaceList <- spaceList |> List.filter (fun s -> s.State.Id <> space.State.Id)
            return Ok()
        }

        member _.ExistsAsync(spaceId: SpaceId) : Task<bool> = task {
            return spaceList |> List.exists (fun s -> s.State.Id = spaceId)
        }

        member _.ExistsByNameAsync(name: string) : Task<bool> = task {
            return spaceList |> List.exists (fun s -> s.State.Name = name)
        }

        member _.GetCountAsync() : Task<int> = task { return spaceList.Length }

// Mock Event Repository for testing
type MockEventRepository() =
    let mutable savedEvents: Freetool.Domain.IDomainEvent list = []

    member _.GetSavedEvents() = savedEvents

    interface IEventRepository with
        member _.SaveEventAsync(event: Freetool.Domain.IDomainEvent) : Task<unit> = task {
            savedEvents <- event :: savedEvents
            return ()
        }

        member _.CommitAsync() : Task<unit> = task { return () }

        member _.GetEventsAsync(_filter: EventFilter) : Task<PagedResult<EventData>> = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 10
            }
        }

        member _.GetEventsByAppIdAsync(_filter: AppEventFilter) : Task<PagedResult<EventData>> = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 10
            }
        }

        member _.GetEventsByDashboardIdAsync(_filter: DashboardEventFilter) : Task<PagedResult<EventData>> = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 10
            }
        }

        member _.GetEventsByUserIdAsync(_filter: UserEventFilter) : Task<PagedResult<EventData>> = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 10
            }
        }

// Mock Authorization Service for testing
type MockAuthorizationService() =
    let mutable permissions: Map<string, bool> = Map.empty

    member this.SetPermission(subject: AuthSubject, relation: AuthRelation, obj: AuthObject, allowed: bool) =
        let key =
            sprintf
                "%s-%s-%s"
                (AuthTypes.subjectToString subject)
                (AuthTypes.relationToString relation)
                (AuthTypes.objectToString obj)

        permissions <- permissions |> Map.add key allowed

    interface IAuthorizationService with
        member _.CreateStoreAsync(_request: CreateStoreRequest) : Task<StoreResponse> = task {
            return { Id = "test-store"; Name = "test" }
        }

        member _.WriteAuthorizationModelAsync() : Task<AuthorizationModelResponse> = task {
            return { AuthorizationModelId = "test-model" }
        }

        member _.InitializeOrganizationAsync (_organizationId: string) (_adminUserId: string) : Task<unit> = task {
            return ()
        }

        member _.CreateRelationshipsAsync(_tuples: RelationshipTuple list) : Task<unit> = task { return () }

        member _.UpdateRelationshipsAsync(_request: UpdateRelationshipsRequest) : Task<unit> = task { return () }

        member _.DeleteRelationshipsAsync(_tuples: RelationshipTuple list) : Task<unit> = task { return () }

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) : Task<bool> = task {
            let key =
                sprintf
                    "%s-%s-%s"
                    (AuthTypes.subjectToString subject)
                    (AuthTypes.relationToString relation)
                    (AuthTypes.objectToString obj)

            return permissions |> Map.tryFind key |> Option.defaultValue false
        }

        member _.StoreExistsAsync(_storeId: string) : Task<bool> = task { return true }

        member this.BatchCheckPermissionsAsync
            (subject: AuthSubject)
            (relations: AuthRelation list)
            (obj: AuthObject)
            : Task<Map<AuthRelation, bool>> =
            task {
                let! results =
                    relations
                    |> List.map (fun relation -> task {
                        let! result = (this :> IAuthorizationService).CheckPermissionAsync subject relation obj
                        return (relation, result)
                    })
                    |> Task.WhenAll

                return results |> Array.toList |> Map.ofList
            }

// Test helper to create a test user
let createTestUser (name: string) (email: string) : ValidatedUser =
    match Email.Create(Some email) with
    | Ok validEmail -> User.create name validEmail None
    | Error _ -> failwith "Failed to create test user"

// Test helper to create a test space
let createTestSpace
    (name: string)
    (actorUserId: UserId)
    (moderatorUserId: UserId)
    (memberIds: UserId list option)
    : ValidatedSpace =
    match Space.create actorUserId name moderatorUserId memberIds with
    | Ok space -> space
    | Error _ -> failwith "Failed to create test space"

// ============================================================================
// CreateSpace Tests
// ============================================================================

[<Fact>]
let ``CreateSpace succeeds with valid data`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let member1 = createTestUser "Member 1" "member1@test.com"
    let member2 = createTestUser "Member 2" "member2@test.com"

    let userRepository =
        MockUserRepository([ moderator; member1; member2 ]) :> IUserRepository

    let spaceRepository = MockSpaceRepository([]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let actorUserId = moderator.State.Id

    let createDto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderator.State.Id.Value.ToString()
        MemberIds = Some [ member1.State.Id.Value.ToString(); member2.State.Id.Value.ToString() ]
    }

    let command = CreateSpace(actorUserId, createDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) ->
        Assert.Equal("Test Space", spaceData.Name)
        Assert.Equal(moderator.State.Id, spaceData.ModeratorUserId)
        Assert.Equal(2, spaceData.MemberIds.Length)
    | _ -> Assert.Fail("Expected SpaceResult")
}

[<Fact>]
let ``CreateSpace fails when name already exists`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"

    let existingSpace =
        createTestSpace "Existing Space" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ existingSpace ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let createDto: CreateSpaceDto = {
        Name = "Existing Space"
        ModeratorUserId = moderator.State.Id.Value.ToString()
        MemberIds = None
    }

    let command = CreateSpace(moderator.State.Id, createDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already exists", msg)
    | _ -> Assert.Fail("Expected Conflict error")
}

[<Fact>]
let ``CreateSpace fails when moderator not found`` () = task {
    // Arrange
    let nonExistentModeratorId = UserId.NewId()

    let userRepository = MockUserRepository([]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let createDto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = nonExistentModeratorId.Value.ToString()
        MemberIds = None
    }

    let command = CreateSpace(nonExistentModeratorId, createDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("Moderator", msg)
    | _ -> Assert.Fail("Expected NotFound error for moderator")
}

[<Fact>]
let ``CreateSpace fails when member not found`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let nonExistentMemberId = UserId.NewId()

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let createDto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderator.State.Id.Value.ToString()
        MemberIds = Some [ nonExistentMemberId.Value.ToString() ]
    }

    let command = CreateSpace(moderator.State.Id, createDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("do not exist", msg)
    | _ -> Assert.Fail("Expected ValidationError for missing member")
}

// ============================================================================
// DeleteSpace Tests
// ============================================================================

[<Fact>]
let ``DeleteSpace removes space`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"

    let space =
        createTestSpace "Space to Delete" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let command = DeleteSpace(moderator.State.Id, space.State.Id.Value.ToString())

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceCommandResult.UnitResult()) -> Assert.True(true) // Success
    | _ -> Assert.Fail("Expected UnitResult")
}

// ============================================================================
// UpdateSpaceName Tests
// ============================================================================

[<Fact>]
let ``UpdateSpaceName changes name`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"

    let space =
        createTestSpace "Original Name" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let updateDto: UpdateSpaceNameDto = { Name = "New Name" }

    let command =
        UpdateSpaceName(moderator.State.Id, space.State.Id.Value.ToString(), updateDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) -> Assert.Equal("New Name", spaceData.Name)
    | _ -> Assert.Fail("Expected SpaceResult with updated name")
}

[<Fact>]
let ``UpdateSpaceName fails for duplicate name`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let space1 = createTestSpace "Space 1" moderator.State.Id moderator.State.Id None
    let space2 = createTestSpace "Space 2" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space1; space2 ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let updateDto: UpdateSpaceNameDto = { Name = "Space 2" } // Already exists

    let command =
        UpdateSpaceName(moderator.State.Id, space1.State.Id.Value.ToString(), updateDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already exists", msg)
    | _ -> Assert.Fail("Expected Conflict error")
}

// ============================================================================
// ChangeModerator Tests
// ============================================================================

[<Fact>]
let ``ChangeModerator updates moderator`` () = task {
    // Arrange
    let originalModerator = createTestUser "Original Moderator" "original@test.com"
    let newModerator = createTestUser "New Moderator" "new@test.com"

    let space =
        createTestSpace "Test Space" originalModerator.State.Id originalModerator.State.Id None

    let userRepository =
        MockUserRepository([ originalModerator; newModerator ]) :> IUserRepository

    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let changeModeratorDto: ChangeModeratorDto = {
        NewModeratorUserId = newModerator.State.Id.Value.ToString()
    }

    let command =
        ChangeModerator(originalModerator.State.Id, space.State.Id.Value.ToString(), changeModeratorDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) -> Assert.Equal(newModerator.State.Id, spaceData.ModeratorUserId)
    | _ -> Assert.Fail("Expected SpaceResult with new moderator")
}

[<Fact>]
let ``ChangeModerator fails when user not found`` () = task {
    // Arrange
    let originalModerator = createTestUser "Original Moderator" "original@test.com"
    let nonExistentUserId = UserId.NewId()

    let space =
        createTestSpace "Test Space" originalModerator.State.Id originalModerator.State.Id None

    let userRepository = MockUserRepository([ originalModerator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let changeModeratorDto: ChangeModeratorDto = {
        NewModeratorUserId = nonExistentUserId.Value.ToString()
    }

    let command =
        ChangeModerator(originalModerator.State.Id, space.State.Id.Value.ToString(), changeModeratorDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("moderator", msg.ToLower())
    | _ -> Assert.Fail("Expected NotFound error")
}

// ============================================================================
// AddMember Tests
// ============================================================================

[<Fact>]
let ``AddMember adds user to space`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let newMember = createTestUser "New Member" "newmember@test.com"
    let space = createTestSpace "Test Space" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator; newMember ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let addMemberDto: AddMemberDto = {
        UserId = newMember.State.Id.Value.ToString()
    }

    let command =
        AddMember(moderator.State.Id, space.State.Id.Value.ToString(), addMemberDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) ->
        Assert.True(List.contains newMember.State.Id spaceData.MemberIds)
        Assert.Equal(1, spaceData.MemberIds.Length)
    | _ -> Assert.Fail("Expected SpaceResult with new member")
}

[<Fact>]
let ``AddMember fails when user not found`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let nonExistentUserId = UserId.NewId()
    let space = createTestSpace "Test Space" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let addMemberDto: AddMemberDto = {
        UserId = nonExistentUserId.Value.ToString()
    }

    let command =
        AddMember(moderator.State.Id, space.State.Id.Value.ToString(), addMemberDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("User", msg)
    | _ -> Assert.Fail("Expected NotFound error")
}

// ============================================================================
// RemoveMember Tests
// ============================================================================

[<Fact>]
let ``RemoveMember removes user from space`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let memberUser = createTestUser "Member" "member@test.com"

    let space =
        createTestSpace "Test Space" moderator.State.Id moderator.State.Id (Some [ memberUser.State.Id ])

    let userRepository =
        MockUserRepository([ moderator; memberUser ]) :> IUserRepository

    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let removeMemberDto: RemoveMemberDto = {
        UserId = memberUser.State.Id.Value.ToString()
    }

    let command =
        RemoveMember(moderator.State.Id, space.State.Id.Value.ToString(), removeMemberDto)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) ->
        Assert.False(List.contains memberUser.State.Id spaceData.MemberIds)
        Assert.Equal(0, spaceData.MemberIds.Length)
    | _ -> Assert.Fail("Expected SpaceResult with member removed")
}

// ============================================================================
// GetSpaceById Tests
// ============================================================================

[<Fact>]
let ``GetSpaceById returns space when exists`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let space = createTestSpace "Test Space" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let command = GetSpaceById(space.State.Id.Value.ToString())

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceResult spaceData) ->
        Assert.Equal("Test Space", spaceData.Name)
        Assert.Equal(space.State.Id, spaceData.Id)
    | _ -> Assert.Fail("Expected SpaceResult")
}

// ============================================================================
// GetSpacesByUserId Tests
// ============================================================================

[<Fact>]
let ``GetSpacesByUserId returns user spaces`` () = task {
    // Arrange
    let user = createTestUser "User" "user@test.com"
    let moderator = createTestUser "Moderator" "moderator@test.com"

    // User is moderator of space1
    let space1 = createTestSpace "Space 1" user.State.Id user.State.Id None
    // User is member of space2
    let space2 =
        createTestSpace "Space 2" moderator.State.Id moderator.State.Id (Some [ user.State.Id ])
    // User is not in space3
    let space3 = createTestSpace "Space 3" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ user; moderator ]) :> IUserRepository

    let spaceRepository =
        MockSpaceRepository([ space1; space2; space3 ]) :> ISpaceRepository

    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let command = GetSpacesByUserId(user.State.Id.Value.ToString(), 0, 10)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpacesResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        // Verify both spaces the user has access to are returned
        let spaceNames = pagedResult.Items |> List.map (fun s -> s.Name) |> List.sort
        Assert.Contains("Space 1", spaceNames)
        Assert.Contains("Space 2", spaceNames)
    | _ -> Assert.Fail("Expected SpacesResult")
}

// ============================================================================
// GetSpaceMembersWithPermissions Tests
// ============================================================================

[<Fact>]
let ``GetSpaceMembersWithPermissions returns permissions`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let memberUser = createTestUser "Member User" "member@test.com"

    let space =
        createTestSpace "Test Space" moderator.State.Id moderator.State.Id (Some [ memberUser.State.Id ])

    let userRepository =
        MockUserRepository([ moderator; memberUser ]) :> IUserRepository

    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository() :> IEventRepository

    let command = GetSpaceMembersWithPermissions(space.State.Id.Value.ToString(), 0, 10)

    // Act
    let! result = SpaceHandler.handleCommand spaceRepository userRepository authService eventRepository command

    // Assert
    match result with
    | Ok(SpaceMembersPermissionsResult response) ->
        Assert.Equal("Test Space", response.SpaceName)
        Assert.Equal(2, response.Members.TotalCount) // moderator + 1 member

        // Find the moderator in results
        let moderatorResult =
            response.Members.Items
            |> List.tryFind (fun m -> m.UserId = moderator.State.Id.Value.ToString())

        Assert.True(moderatorResult.IsSome)
        Assert.True(moderatorResult.Value.IsModerator)
        // Moderators should have all permissions
        Assert.True(moderatorResult.Value.Permissions.CreateApp)
        Assert.True(moderatorResult.Value.Permissions.EditApp)
        Assert.True(moderatorResult.Value.Permissions.DeleteApp)
    | _ -> Assert.Fail("Expected SpaceMembersPermissionsResult")
}

// ============================================================================
// Default Member Permissions Tests
// ============================================================================

[<Fact>]
let ``UpdateDefaultMemberPermissions writes audit event when permissions change`` () = task {
    // Arrange
    let moderator = createTestUser "Moderator User" "moderator@test.com"
    let space = createTestSpace "Engineering" moderator.State.Id moderator.State.Id None

    let userRepository = MockUserRepository([ moderator ]) :> IUserRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = MockAuthorizationService() :> IAuthorizationService
    let eventRepository = MockEventRepository()

    let updateDto: UpdateDefaultMemberPermissionsDto = {
        Permissions = {
            CreateResource = true
            EditResource = false
            DeleteResource = false
            CreateApp = true
            EditApp = false
            DeleteApp = false
            RunApp = false
            CreateDashboard = true
            EditDashboard = false
            DeleteDashboard = false
            RunDashboard = false
            CreateFolder = false
            EditFolder = false
            DeleteFolder = false
        }
    }

    let command =
        UpdateDefaultMemberPermissions(moderator.State.Id, space.State.Id.Value.ToString(), updateDto)

    // Act
    let! result =
        SpaceHandler.handleCommand
            spaceRepository
            userRepository
            authService
            (eventRepository :> IEventRepository)
            command

    // Assert
    match result with
    | Ok(SpaceCommandResult.UnitResult()) ->
        let savedEvents = eventRepository.GetSavedEvents()

        let defaultPermissionsEvent =
            savedEvents
            |> List.tryFind (fun e -> e :? SpaceDefaultMemberPermissionsChangedEvent)

        Assert.True(defaultPermissionsEvent.IsSome)

        let typedEvent =
            defaultPermissionsEvent.Value :?> SpaceDefaultMemberPermissionsChangedEvent

        Assert.Equal(space.State.Id, typedEvent.SpaceId)
        Assert.Equal("Engineering", typedEvent.SpaceName)
        Assert.Contains("CreateResource", typedEvent.PermissionsGranted)
        Assert.Contains("CreateApp", typedEvent.PermissionsGranted)
    | _ -> Assert.Fail("Expected UnitResult")
}