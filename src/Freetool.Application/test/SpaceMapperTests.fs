module Freetool.Application.Tests.SpaceMapperTests

open System
open Xunit
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Mappers

// ============================================================================
// SpaceMapper fromCreateDto Tests
// ============================================================================

[<Fact>]
let ``SpaceMapper fromCreateDto with no MemberIds should return moderator and empty list`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds = None
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(moderatorUserId, name, memberIds) ->
        Assert.Equal("Test Space", name)
        Assert.Equal(moderatorGuid, moderatorUserId.Value)
        Assert.Empty(memberIds)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with empty MemberIds list should return empty member list`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds = Some []
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(moderatorUserId, name, memberIds) ->
        Assert.Equal("Test Space", name)
        Assert.Equal(moderatorGuid, moderatorUserId.Value)
        Assert.Empty(memberIds)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with valid MemberIds should parse members`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()
    let member1Guid = Guid.NewGuid()
    let member2Guid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds = Some [ member1Guid.ToString(); member2Guid.ToString() ]
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(moderatorUserId, name, memberIds) ->
        Assert.Equal("Test Space", name)
        Assert.Equal(moderatorGuid, moderatorUserId.Value)
        Assert.Equal(2, memberIds.Length)
        let memberIdValues = memberIds |> List.map (fun uid -> uid.Value)
        Assert.Contains(member1Guid, memberIdValues)
        Assert.Contains(member2Guid, memberIdValues)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with invalid MemberIds should filter out invalid GUIDs`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()
    let validGuid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds = Some [ validGuid.ToString(); "invalid-guid"; "not-a-guid"; "" ]
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(_, _, memberIds) ->
        Assert.Single(memberIds) |> ignore
        Assert.Equal(validGuid, memberIds.[0].Value)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with duplicate MemberIds should remove duplicates`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()
    let member1Guid = Guid.NewGuid()
    let member2Guid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds =
            Some [
                member1Guid.ToString()
                member2Guid.ToString()
                member1Guid.ToString() // Duplicate
                member2Guid.ToString()
            ] // Duplicate
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(_, _, memberIds) ->
        Assert.Equal(2, memberIds.Length) // 2 unique members
        let memberIdValues = memberIds |> List.map (fun uid -> uid.Value)
        Assert.Contains(member1Guid, memberIdValues)
        Assert.Contains(member2Guid, memberIdValues)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with moderator in MemberIds should filter out moderator`` () =
    // Arrange
    let moderatorGuid = Guid.NewGuid()
    let member1Guid = Guid.NewGuid()

    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = moderatorGuid.ToString()
        MemberIds =
            Some [
                moderatorGuid.ToString() // Moderator is also in members
                member1Guid.ToString()
            ]
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok(_, _, memberIds) ->
        // Moderator should be filtered out, only member1 should be in the list
        Assert.Single(memberIds) |> ignore
        Assert.Equal(member1Guid, memberIds.[0].Value)
    | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

[<Fact>]
let ``SpaceMapper fromCreateDto with invalid moderator ID should return error`` () =
    // Arrange
    let dto: CreateSpaceDto = {
        Name = "Test Space"
        ModeratorUserId = "invalid-guid"
        MemberIds = None
    }

    // Act
    let result = SpaceMapper.fromCreateDto dto

    // Assert
    match result with
    | Ok _ -> Assert.Fail("Expected Error but got Ok")
    | Error msg -> Assert.Contains("Invalid moderator", msg)

// ============================================================================
// SpaceMapper toDto Tests
// ============================================================================

[<Fact>]
let ``SpaceMapper toDto should map all properties correctly`` () =
    // Arrange
    let spaceId = SpaceId.NewId()
    let moderatorId = UserId.NewId()
    let member1 = UserId.NewId()
    let member2 = UserId.NewId()
    let createdAt = DateTime.UtcNow.AddDays(-1.0)
    let updatedAt = DateTime.UtcNow

    let spaceData: SpaceData = {
        Id = spaceId
        Name = "Engineering Space"
        ModeratorUserId = moderatorId
        MemberIds = [ moderatorId; member1; member2 ]
        CreatedAt = createdAt
        UpdatedAt = updatedAt
        IsDeleted = false
    }

    // Act
    let dto = SpaceMapper.toDto spaceData

    // Assert
    Assert.Equal(spaceId.Value.ToString(), dto.Id)
    Assert.Equal("Engineering Space", dto.Name)
    Assert.Equal(moderatorId.Value.ToString(), dto.ModeratorUserId)
    Assert.Equal(3, dto.MemberIds.Length)
    Assert.Contains(moderatorId.Value.ToString(), dto.MemberIds)
    Assert.Contains(member1.Value.ToString(), dto.MemberIds)
    Assert.Contains(member2.Value.ToString(), dto.MemberIds)
    Assert.Equal(createdAt, dto.CreatedAt)
    Assert.Equal(updatedAt, dto.UpdatedAt)

[<Fact>]
let ``SpaceMapper toDto should handle space with only moderator`` () =
    // Arrange
    let spaceId = SpaceId.NewId()
    let moderatorId = UserId.NewId()

    let spaceData: SpaceData = {
        Id = spaceId
        Name = "Solo Space"
        ModeratorUserId = moderatorId
        MemberIds = [ moderatorId ]
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    // Act
    let dto = SpaceMapper.toDto spaceData

    // Assert
    Assert.Equal("Solo Space", dto.Name)
    Assert.Single(dto.MemberIds) |> ignore
    Assert.Equal(moderatorId.Value.ToString(), dto.MemberIds.[0])

// ============================================================================
// SpaceMapper toPagedDto Tests
// ============================================================================

[<Fact>]
let ``SpaceMapper toPagedDto should map paging info correctly`` () =
    // Arrange
    let spaceId1 = SpaceId.NewId()
    let spaceId2 = SpaceId.NewId()
    let moderatorId = UserId.NewId()

    let spaces: SpaceData list = [
        {
            Id = spaceId1
            Name = "Space 1"
            ModeratorUserId = moderatorId
            MemberIds = [ moderatorId ]
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
        {
            Id = spaceId2
            Name = "Space 2"
            ModeratorUserId = moderatorId
            MemberIds = [ moderatorId ]
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
        }
    ]

    // Act
    let pagedResult = SpaceMapper.toPagedDto spaces 10 5 2

    // Assert
    Assert.Equal(2, pagedResult.Items.Length)
    Assert.Equal(10, pagedResult.TotalCount)
    Assert.Equal(5, pagedResult.Skip)
    Assert.Equal(2, pagedResult.Take)
    Assert.Equal("Space 1", pagedResult.Items.[0].Name)
    Assert.Equal("Space 2", pagedResult.Items.[1].Name)