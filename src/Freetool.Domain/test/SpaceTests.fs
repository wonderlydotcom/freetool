module Freetool.Domain.Tests.SpaceTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

// Helper function for test assertions
let unwrapResult result =
    match result with
    | Ok value -> value
    | Error error -> failwith $"Expected Ok but got Error: {error}"

// ============================================================================
// Space Creation Tests
// ============================================================================

[<Fact>]
let ``Space.create should succeed with valid name and moderator`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let name = "Engineering"

    // Act
    let result = Space.create actorUserId name moderatorId None

    // Assert
    match result with
    | Ok space ->
        Assert.Equal(name, space.State.Name)
        Assert.Equal(moderatorId, space.State.ModeratorUserId)
        Assert.Single(space.UncommittedEvents) |> ignore

        match space.UncommittedEvents.[0] with
        | :? SpaceCreatedEvent as event ->
            Assert.Equal(name, event.Name)
            Assert.Equal(moderatorId, event.ModeratorUserId)
            Assert.Equal(space.State.Id, event.SpaceId)
        | _ -> Assert.True(false, "Expected SpaceCreatedEvent")
    | Error _ -> Assert.Fail("Expected Ok result")

[<Fact>]
let ``Space.create should fail with empty name`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())

    // Act
    let result = Space.create actorUserId "" moderatorId None

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("empty", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected ValidationError for empty name")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

[<Fact>]
let ``Space.create should fail with whitespace-only name`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())

    // Act
    let result = Space.create actorUserId "   " moderatorId None

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("empty", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected ValidationError for whitespace-only name")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

[<Fact>]
let ``Space.create should fail with name exceeding 100 characters`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let longName = String.replicate 101 "a"

    // Act
    let result = Space.create actorUserId longName moderatorId None

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("100", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for long name")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

[<Fact>]
let ``Space.create should succeed with exactly 100 characters name`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let maxLengthName = String.replicate 100 "a"

    // Act
    let result = Space.create actorUserId maxLengthName moderatorId None

    // Assert
    match result with
    | Ok space -> Assert.Equal(100, space.State.Name.Length)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.create should trim name`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())

    // Act
    let result = Space.create actorUserId "  Engineering Team  " moderatorId None

    // Assert
    match result with
    | Ok space -> Assert.Equal("Engineering Team", space.State.Name)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.create should NOT include moderator in member list (moderator is stored separately)`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())

    // Act
    let result = Space.create actorUserId "Engineering" moderatorId None

    // Assert - Moderator is stored in ModeratorUserId, not in MemberIds
    match result with
    | Ok space ->
        Assert.Equal(moderatorId, space.State.ModeratorUserId)
        Assert.DoesNotContain(moderatorId, space.State.MemberIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.create with initial members should include all members but not moderator`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()
    let initialMembers = Some [ member1; member2 ]

    // Act
    let result = Space.create actorUserId "Engineering" moderatorId initialMembers

    // Assert - Moderator should NOT be in MemberIds (stored separately)
    match result with
    | Ok space ->
        Assert.Equal(moderatorId, space.State.ModeratorUserId)
        Assert.DoesNotContain(moderatorId, space.State.MemberIds)
        Assert.Contains(member1, space.State.MemberIds)
        Assert.Contains(member2, space.State.MemberIds)
        Assert.Equal(2, space.State.MemberIds.Length)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.create with duplicate members should remove duplicates`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let member1 = UserId.NewId()
    let initialMembers = Some [ member1; member1; member1 ] // Duplicates

    // Act
    let result = Space.create actorUserId "Engineering" moderatorId initialMembers

    // Assert
    match result with
    | Ok space ->
        // Should have moderator + 1 unique member (not 3 duplicates)
        let distinctMembers = space.State.MemberIds |> List.distinct
        Assert.Equal(space.State.MemberIds.Length, distinctMembers.Length)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

// ============================================================================
// Space Name Update Tests
// ============================================================================

[<Fact>]
let ``Space.updateName should create NameChanged event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Old Name" moderatorId None |> unwrapResult

    // Act
    let result = Space.updateName actorUserId "New Name" space

    // Assert
    match result with
    | Ok updatedSpace ->
        let events = Space.getUncommittedEvents updatedSpace
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with
        | :? SpaceUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | SpaceChange.NameChanged(oldValue, newValue) ->
                Assert.Equal("Old Name", oldValue)
                Assert.Equal("New Name", newValue)
            | _ -> Assert.Fail("Expected NameChanged event")
        | _ -> Assert.Fail("Expected SpaceUpdatedEvent")

        // Verify updated state
        Assert.Equal("New Name", Space.getName updatedSpace)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.updateName with same name should not generate event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Same Name" moderatorId None |> unwrapResult

    // Act
    let result = Space.updateName actorUserId "Same Name" space

    // Assert
    match result with
    | Ok updatedSpace ->
        let events = Space.getUncommittedEvents updatedSpace
        Assert.Single(events) |> ignore // Only creation event, no update event
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.updateName with empty name should fail`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Valid Name" moderatorId None |> unwrapResult

    // Act
    let result = Space.updateName actorUserId "" space

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("empty", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected ValidationError for empty name")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

[<Fact>]
let ``Space.updateName with name exceeding 100 characters should fail`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderatorId = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Valid Name" moderatorId None |> unwrapResult
    let longName = String.replicate 101 "a"

    // Act
    let result = Space.updateName actorUserId longName space

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Contains("100", msg)
    | Ok _ -> Assert.Fail("Expected ValidationError for long name")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

// ============================================================================
// Space Moderator Change Tests
// ============================================================================

[<Fact>]
let ``Space.changeModerator should update moderator and create event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let originalModerator = UserId.FromGuid(Guid.NewGuid())
    let newModerator = UserId.FromGuid(Guid.NewGuid())

    let space =
        Space.create actorUserId "Engineering" originalModerator None |> unwrapResult

    // Act
    let result = Space.changeModerator actorUserId newModerator space

    // Assert
    match result with
    | Ok updatedSpace ->
        Assert.Equal(newModerator, updatedSpace.State.ModeratorUserId)

        let events = Space.getUncommittedEvents updatedSpace
        Assert.Equal(2, events.Length) // Creation + change event

        match events.[1] with
        | :? SpaceUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | SpaceChange.ModeratorChanged(oldMod, newMod) ->
                Assert.Equal(originalModerator, oldMod)
                Assert.Equal(newModerator, newMod)
            | _ -> Assert.Fail("Expected ModeratorChanged event")
        | _ -> Assert.Fail("Expected SpaceUpdatedEvent")
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.changeModerator should set new moderator correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let originalModerator = UserId.FromGuid(Guid.NewGuid())
    let newModerator = UserId.FromGuid(Guid.NewGuid())

    let space =
        Space.create actorUserId "Engineering" originalModerator None |> unwrapResult

    // Act
    let result = Space.changeModerator actorUserId newModerator space

    // Assert
    match result with
    | Ok updatedSpace ->
        // New moderator should be set as ModeratorUserId
        Assert.Equal(newModerator, updatedSpace.State.ModeratorUserId)
        // New moderator should NOT be in members (moderator is stored separately)
        Assert.DoesNotContain(newModerator, updatedSpace.State.MemberIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.changeModerator with same moderator should not generate event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act
    let result = Space.changeModerator actorUserId moderator space

    // Assert
    match result with
    | Ok updatedSpace ->
        let events = Space.getUncommittedEvents updatedSpace
        Assert.Single(events) |> ignore // Only creation event
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

// ============================================================================
// Space Member Add Tests
// ============================================================================

[<Fact>]
let ``Space.addMember should add member to list`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let newMember = UserId.NewId()
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act
    let result = Space.addMember actorUserId newMember space

    // Assert
    match result with
    | Ok updatedSpace ->
        Assert.Contains(newMember, updatedSpace.State.MemberIds)

        let events = Space.getUncommittedEvents updatedSpace
        Assert.Equal(2, events.Length) // Creation + add member event

        match events.[1] with
        | :? SpaceUpdatedEvent as event ->
            match event.Changes.[0] with
            | SpaceChange.MemberAdded(addedId) -> Assert.Equal(newMember, addedId)
            | _ -> Assert.Fail("Expected MemberAdded event")
        | _ -> Assert.Fail("Expected SpaceUpdatedEvent")
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.addMember should fail if member already exists`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let existingMember = UserId.NewId()

    let space =
        Space.create actorUserId "Engineering" moderator (Some [ existingMember ])
        |> unwrapResult

    // Act - Try to add same member again
    let result = Space.addMember actorUserId existingMember space

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected Conflict error for duplicate member")
    | Error error -> Assert.Fail($"Expected Conflict but got: {error}")

[<Fact>]
let ``Space.addMember should fail if adding moderator who is already a member`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act - Try to add moderator as member (they should already be in the list)
    let result = Space.addMember actorUserId moderator space

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected Conflict error - moderator is already a member")
    | Error error -> Assert.Fail($"Expected Conflict but got: {error}")

// ============================================================================
// Space Member Remove Tests
// ============================================================================

[<Fact>]
let ``Space.removeMember should remove member from list`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()

    let space =
        Space.create actorUserId "Engineering" moderator (Some [ member1; member2 ])
        |> unwrapResult

    // Act
    let result = Space.removeMember actorUserId member1 space

    // Assert
    match result with
    | Ok updatedSpace ->
        Assert.DoesNotContain(member1, updatedSpace.State.MemberIds)
        Assert.Contains(member2, updatedSpace.State.MemberIds) // member2 still there

        let events = Space.getUncommittedEvents updatedSpace
        Assert.Equal(2, events.Length) // Creation + remove member event

        match events.[1] with
        | :? SpaceUpdatedEvent as event ->
            match event.Changes.[0] with
            | SpaceChange.MemberRemoved(removedId) -> Assert.Equal(member1, removedId)
            | _ -> Assert.Fail("Expected MemberRemoved event")
        | _ -> Assert.Fail("Expected SpaceUpdatedEvent")
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.removeMember should fail when trying to remove moderator`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act - Try to remove the moderator
    let result = Space.removeMember actorUserId moderator space

    // Assert
    match result with
    | Error(InvalidOperation msg) -> Assert.Contains("moderator", msg.ToLower())
    | Error(ValidationError msg) -> Assert.Contains("moderator", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected error when trying to remove moderator")
    | Error error -> Assert.Fail($"Expected InvalidOperation or ValidationError but got: {error}")

[<Fact>]
let ``Space.removeMember should fail if member not in space`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let nonExistentMember = UserId.NewId()
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act
    let result = Space.removeMember actorUserId nonExistentMember space

    // Assert
    match result with
    | Error(NotFound msg) -> Assert.Contains("member", msg.ToLower())
    | Ok _ -> Assert.Fail("Expected NotFound error for non-existent member")
    | Error error -> Assert.Fail($"Expected NotFound but got: {error}")

// ============================================================================
// Space Deletion Tests
// ============================================================================

[<Fact>]
let ``Space.markForDeletion should generate SpaceDeletedEvent`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act
    let deletedSpace = Space.markForDeletion actorUserId space

    // Assert
    let events = Space.getUncommittedEvents deletedSpace
    Assert.Equal(2, events.Length) // Creation event + deletion event

    match events.[1] with
    | :? SpaceDeletedEvent as event -> Assert.Equal(space.State.Id, event.SpaceId)
    | _ -> Assert.Fail("Expected SpaceDeletedEvent")

// ============================================================================
// Space Validation Tests
// ============================================================================

[<Fact>]
let ``Space.validate should trim name and succeed`` () =
    // Arrange
    let moderator = UserId.FromGuid(Guid.NewGuid())

    let space = {
        State = {
            Id = SpaceId.NewId()
            Name = "  Engineering Team  "
            ModeratorUserId = moderator
            MemberIds = [ moderator ]
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
        UncommittedEvents = []
    }

    // Act
    let result = Space.validate space

    // Assert
    match result with
    | Ok validatedSpace -> Assert.Equal("Engineering Team", validatedSpace.State.Name)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.validate should succeed when moderator is NOT in members (separate role)`` () =
    // Arrange - Moderator is stored separately from members by design
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let otherMember = UserId.NewId()

    let space = {
        State = {
            Id = SpaceId.NewId()
            Name = "Engineering Team"
            ModeratorUserId = moderator
            MemberIds = [ otherMember ] // Moderator not in members - this is correct
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
        UncommittedEvents = []
    }

    // Act
    let result = Space.validate space

    // Assert - Validation should succeed; moderator is stored separately
    match result with
    | Ok validatedSpace ->
        Assert.Equal(moderator, validatedSpace.State.ModeratorUserId)
        Assert.DoesNotContain(moderator, validatedSpace.State.MemberIds)
        Assert.Contains(otherMember, validatedSpace.State.MemberIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.validate should remove duplicate members`` () =
    // Arrange
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let member1 = UserId.NewId()

    let space = {
        State = {
            Id = SpaceId.NewId()
            Name = "Engineering Team"
            ModeratorUserId = moderator
            MemberIds = [ moderator; member1; member1; member1 ] // Duplicates
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
        UncommittedEvents = []
    }

    // Act
    let result = Space.validate space

    // Assert
    match result with
    | Ok validatedSpace ->
        let memberCount = validatedSpace.State.MemberIds.Length
        let distinctCount = validatedSpace.State.MemberIds |> List.distinct |> List.length
        Assert.Equal(distinctCount, memberCount)
        // Expected: 1 (only member1, moderator is filtered out of MemberIds)
        Assert.Equal(1, memberCount)
        Assert.Contains(member1, validatedSpace.State.MemberIds)
        Assert.DoesNotContain(moderator, validatedSpace.State.MemberIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

// ============================================================================
// Space fromData Tests
// ============================================================================

[<Fact>]
let ``Space.fromData should have no uncommitted events`` () =
    // Arrange
    let moderator = UserId.FromGuid(Guid.NewGuid())

    let spaceData = {
        Id = SpaceId.NewId()
        Name = "Test Space"
        ModeratorUserId = moderator
        MemberIds = [ moderator ]
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    // Act
    let space = Space.fromData spaceData

    // Assert
    let events = Space.getUncommittedEvents space
    Assert.Empty(events)
    Assert.Equal("Test Space", Space.getName space)

// ============================================================================
// Space Event Commit Tests
// ============================================================================

[<Fact>]
let ``Space.markEventsAsCommitted should clear uncommitted events`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let memberId = UserId.NewId()
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult
    let spaceWithMember = Space.addMember actorUserId memberId space |> unwrapResult

    // Verify we have events before committing
    let eventsBefore = Space.getUncommittedEvents spaceWithMember
    Assert.Equal(2, eventsBefore.Length)

    // Act
    let committedSpace = Space.markEventsAsCommitted spaceWithMember

    // Assert
    let eventsAfter = Space.getUncommittedEvents committedSpace
    Assert.Empty(eventsAfter)

    // Verify state is preserved
    Assert.Equal("Engineering", Space.getName committedSpace)
    Assert.True(Space.hasMember memberId committedSpace)

// ============================================================================
// Space Helper Function Tests
// ============================================================================

[<Fact>]
let ``Space.hasMember should return true for existing member`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let testMember = UserId.NewId()

    let space =
        Space.create actorUserId "Engineering" moderator (Some [ testMember ])
        |> unwrapResult

    // Act & Assert
    Assert.True(Space.hasMember testMember space)
    // Moderator is NOT a member - they have a separate role
    Assert.False(Space.hasMember moderator space)
    // But moderator should have access via hasAccess
    Assert.True(Space.hasAccess moderator space)

[<Fact>]
let ``Space.hasMember should return false for non-member`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let nonMember = UserId.NewId()
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act & Assert
    Assert.False(Space.hasMember nonMember space)

[<Fact>]
let ``Space.isModerator should return true for moderator`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let space = Space.create actorUserId "Engineering" moderator None |> unwrapResult

    // Act & Assert
    Assert.True(Space.isModerator moderator space)

[<Fact>]
let ``Space.isModerator should return false for non-moderator`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let moderator = UserId.FromGuid(Guid.NewGuid())
    let testMember = UserId.NewId()

    let space =
        Space.create actorUserId "Engineering" moderator (Some [ testMember ])
        |> unwrapResult

    // Act & Assert
    Assert.False(Space.isModerator testMember space)

// ============================================================================
// Space validateMemberIds Tests
// ============================================================================

[<Fact>]
let ``Space.validateMemberIds with valid members should succeed`` () =
    // Arrange
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()
    let memberIds = [ member1; member2 ]
    let userExistsFunc _ = true // All users exist

    // Act
    let result = Space.validateMemberIds memberIds userExistsFunc

    // Assert
    match result with
    | Ok validatedIds ->
        Assert.Equal(2, validatedIds.Length)
        Assert.Contains(member1, validatedIds)
        Assert.Contains(member2, validatedIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.validateMemberIds with invalid members should fail`` () =
    // Arrange
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()
    let member3 = UserId.NewId()
    let memberIds = [ member1; member2; member3 ]

    // Only member1 exists
    let userExistsFunc userId = userId = member1

    // Act
    let result = Space.validateMemberIds memberIds userExistsFunc

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains(member2.Value.ToString(), message)
        Assert.Contains(member3.Value.ToString(), message)
        Assert.Contains("do not exist", message)
    | Ok _ -> Assert.Fail("Expected validation error for invalid members")
    | Error error -> Assert.Fail($"Expected ValidationError but got: {error}")

[<Fact>]
let ``Space.validateMemberIds with duplicate members should remove duplicates`` () =
    // Arrange
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()
    let memberIds = [ member1; member2; member1; member2 ] // Duplicates
    let userExistsFunc _ = true // All users exist

    // Act
    let result = Space.validateMemberIds memberIds userExistsFunc

    // Assert
    match result with
    | Ok validatedIds ->
        Assert.Equal(2, validatedIds.Length) // Should only have 2 unique members
        Assert.Contains(member1, validatedIds)
        Assert.Contains(member2, validatedIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")

[<Fact>]
let ``Space.validateMemberIds with empty list should succeed`` () =
    // Arrange
    let memberIds = []
    let userExistsFunc _ = true

    // Act
    let result = Space.validateMemberIds memberIds userExistsFunc

    // Assert
    match result with
    | Ok validatedIds -> Assert.Empty(validatedIds)
    | Error error -> Assert.Fail($"Expected success but got error: {error}")