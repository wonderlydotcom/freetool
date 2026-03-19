module Freetool.Domain.Tests.FolderTests

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
// Folder Creation Tests (with SpaceId)
// ============================================================================

[<Fact>]
let ``Folder creation should generate FolderCreatedEvent`` () =
    // Arrange & Act
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()
    let result = Folder.create actorUserId "My Documents" None spaceId

    // Assert
    match result with
    | Ok folder ->
        let events = Folder.getUncommittedEvents folder
        Assert.Single(events) |> ignore

        match events.[0] with
        | :? FolderCreatedEvent as event ->
            Assert.Equal("My Documents", event.Name.Value)
            Assert.Equal(None, event.ParentId)
            Assert.Equal(spaceId, event.SpaceId)
        | _ -> Assert.True(false, "Expected FolderCreatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Folder creation with parent should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let parentId = FolderId.NewId()
    let spaceId = SpaceId.NewId()

    // Act
    let result = Folder.create actorUserId "Sub Folder" (Some parentId) spaceId

    // Assert
    match result with
    | Ok folder ->
        let events = Folder.getUncommittedEvents folder
        Assert.Single(events) |> ignore

        match events.[0] with
        | :? FolderCreatedEvent as event ->
            Assert.Equal("Sub Folder", event.Name.Value)
            Assert.Equal(Some parentId, event.ParentId)
            Assert.Equal(spaceId, event.SpaceId)
        | _ -> Assert.True(false, "Expected FolderCreatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Folder name update should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()

    let folder =
        Folder.create actorUserId "Old Folder Name" None spaceId |> unwrapResult

    // Act
    let result = Folder.updateName actorUserId "New Folder Name" folder

    // Assert
    match result with
    | Ok updatedFolder ->
        let events = Folder.getUncommittedEvents updatedFolder
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? FolderUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | FolderChange.NameChanged(oldValue, newValue) ->
                Assert.Equal("Old Folder Name", oldValue.Value)
                Assert.Equal("New Folder Name", newValue.Value)
            | _ -> Assert.True(false, "Expected NameChanged event")
        | _ -> Assert.True(false, "Expected FolderUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Folder move to parent should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()

    let folder = Folder.create actorUserId "Mobile Folder" None spaceId |> unwrapResult

    let newParentId = FolderId.NewId()

    // Act
    let movedFolder = Folder.moveToParent actorUserId (Some newParentId) folder

    // Assert
    let events = Folder.getUncommittedEvents movedFolder
    Assert.Equal(2, events.Length) // Creation event + update event

    match events.[1] with // The update event is the second one
    | :? FolderUpdatedEvent as event ->
        Assert.Single(event.Changes) |> ignore

        match event.Changes.[0] with
        | FolderChange.ParentChanged(oldParentId, newParentIdValue) ->
            Assert.Equal(None, oldParentId)
            Assert.Equal(Some newParentId, newParentIdValue)
        | _ -> Assert.True(false, "Expected ParentChanged event")
    | _ -> Assert.True(false, "Expected FolderUpdatedEvent")

[<Fact>]
let ``Folder move to root should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let parentId = FolderId.NewId()
    let spaceId = SpaceId.NewId()

    let folder =
        Folder.create actorUserId "Child Folder" (Some parentId) spaceId |> unwrapResult

    // Act
    let movedFolder = Folder.moveToParent actorUserId None folder

    // Assert
    let events = Folder.getUncommittedEvents movedFolder
    Assert.Equal(2, events.Length) // Creation event + update event

    match events.[1] with // The update event is the second one
    | :? FolderUpdatedEvent as event ->
        Assert.Single(event.Changes) |> ignore

        match event.Changes.[0] with
        | FolderChange.ParentChanged(oldParentId, newParentIdValue) ->
            Assert.Equal(Some parentId, oldParentId)
            Assert.Equal(None, newParentIdValue)
        | _ -> Assert.True(false, "Expected ParentChanged event")
    | _ -> Assert.True(false, "Expected FolderUpdatedEvent")

[<Fact>]
let ``Folder creation should reject empty name`` () =
    // Act
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()
    let result = Folder.create actorUserId "" None spaceId

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("Folder name cannot be empty", msg)
    | _ -> Assert.True(false, "Expected validation error for empty name")

[<Fact>]
let ``Folder creation should reject name longer than 100 characters`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let longName = String.replicate 101 "a"
    let spaceId = SpaceId.NewId()

    // Act
    let result = Folder.create actorUserId longName None spaceId

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("Folder name cannot exceed 100 characters", msg)
    | _ -> Assert.True(false, "Expected validation error for long name")

[<Fact>]
let ``Folder deletion should generate FolderDeletedEvent`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()

    let folder = Folder.create actorUserId "Test Folder" None spaceId |> unwrapResult

    // Act
    let deletedFolder = Folder.markForDeletion actorUserId folder

    // Assert
    let events = Folder.getUncommittedEvents deletedFolder
    Assert.Equal(2, events.Length) // Creation event + deletion event

    match events.[1] with // The deletion event is the second one
    | :? FolderDeletedEvent as event -> Assert.Equal(folder.State.Id, event.FolderId)
    | _ -> Assert.True(false, "Expected FolderDeletedEvent")

[<Fact>]
let ``Root folder should be identified correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()

    let rootFolder =
        Folder.create actorUserId "Root Folder" None spaceId |> unwrapResult

    let parentId = FolderId.NewId()

    let childFolder =
        Folder.create actorUserId "Child Folder" (Some parentId) spaceId |> unwrapResult

    // Act & Assert
    Assert.True(Folder.isRoot rootFolder)
    Assert.False(Folder.isRoot childFolder)

// ============================================================================
// Folder SpaceId Tests
// ============================================================================

[<Fact>]
let ``Folder should store SpaceId correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()

    // Act
    let result = Folder.create actorUserId "Test Folder" None spaceId

    // Assert
    match result with
    | Ok folder -> Assert.Equal(spaceId, folder.State.SpaceId)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Folder.getSpaceId should return correct SpaceId`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.NewId()
    let folder = Folder.create actorUserId "Test Folder" None spaceId |> unwrapResult

    // Act
    let returnedSpaceId = Folder.getSpaceId folder

    // Assert
    Assert.Equal(spaceId, returnedSpaceId)