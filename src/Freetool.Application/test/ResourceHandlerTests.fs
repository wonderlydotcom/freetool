module Freetool.Application.Tests.ResourceHandlerTests

open System
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Commands
open Freetool.Application.Handlers
open Freetool.Application.Interfaces

// Mock repository for testing
type MockResourceRepository(resources: ValidatedResource list) =
    let mutable resourceList = resources
    let mutable nameConflicts = Map.empty<string, bool>

    member _.SetNameConflict(name: string, spaceId: SpaceId, hasConflict: bool) =
        nameConflicts <- nameConflicts.Add($"{name}_{spaceId.Value}", hasConflict)

    interface IResourceRepository with
        member _.GetByIdAsync(resourceId: ResourceId) : Task<ValidatedResource option> = task {
            return resourceList |> List.tryFind (fun r -> r.State.Id = resourceId)
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedResource list> = task {
            return resourceList |> List.skip skip |> List.truncate take
        }

        member _.GetBySpaceAsync (spaceId: SpaceId) (skip: int) (take: int) : Task<ValidatedResource list> = task {
            return
                resourceList
                |> List.filter (fun r -> r.State.SpaceId = spaceId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetCountBySpaceAsync(spaceId: SpaceId) : Task<int> = task {
            return resourceList |> List.filter (fun r -> r.State.SpaceId = spaceId) |> List.length
        }

        member _.AddAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            resourceList <- resource :: resourceList
            return Ok()
        }

        member _.UpdateAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            resourceList <-
                resource
                :: (resourceList |> List.filter (fun r -> r.State.Id <> resource.State.Id))

            return Ok()
        }

        member _.DeleteAsync(resource: ValidatedResource) : Task<Result<unit, DomainError>> = task {
            resourceList <- resourceList |> List.filter (fun r -> r.State.Id <> resource.State.Id)
            return Ok()
        }

        member _.ExistsAsync(resourceId: ResourceId) : Task<bool> = task {
            return resourceList |> List.exists (fun r -> r.State.Id = resourceId)
        }

        member _.ExistsByNameAsync(resourceName: ResourceName) : Task<bool> = task {
            return resourceList |> List.exists (fun r -> r.State.Name = resourceName)
        }

        member _.GetCountAsync() : Task<int> = task { return resourceList.Length }

        member _.GetDeletedBySpaceAsync(_spaceId: SpaceId) : Task<ValidatedResource list> = task { return [] }

        member _.GetDeletedByIdAsync(_resourceId: ResourceId) : Task<ValidatedResource option> = task { return None }

        member _.RestoreAsync(_resource: ValidatedResource) : Task<Result<unit, DomainError>> = task { return Ok() }

        member _.CheckNameConflictAsync (name: ResourceName) (spaceId: SpaceId) : Task<bool> = task {
            let key = $"{name.Value}_{spaceId.Value}"

            match nameConflicts.TryFind key with
            | Some value -> return value
            | None ->
                // Default: check if name exists in the same space
                return
                    resourceList
                    |> List.exists (fun r -> r.State.Name = name && r.State.SpaceId = spaceId)
        }

// Mock app repository for testing
type MockAppRepository() =
    interface IAppRepository with
        member _.GetByIdAsync(_appId: AppId) = task { return None }
        member _.GetByNameAndFolderIdAsync _ _ = task { return None }
        member _.GetByFolderIdAsync _ _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }
        member _.AddAsync(_app: ValidatedApp) = task { return Ok() }
        member _.UpdateAsync(_app: ValidatedApp) = task { return Ok() }
        member _.DeleteAsync _ _ = task { return Ok() }
        member _.ExistsAsync(_appId: AppId) = task { return false }
        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetByResourceIdAsync _ = task { return [] }
        member _.GetDeletedByFolderIdsAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

// Test helper to create a resource
let createTestResource (name: string) (spaceId: SpaceId) : ValidatedResource =
    match Resource.create (UserId.NewId()) spaceId name "Description" "https://api.example.com" [] [] [] with
    | Ok resource -> resource
    | Error _ -> failwith "Failed to create test resource"

[<Fact>]
let ``GetAllResources returns only resources from specified space`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let space2 = SpaceId.NewId()

    let resources = [
        createTestResource "Resource 1" space1
        createTestResource "Resource 2" space1
        createTestResource "Resource 3" space2
        createTestResource "Resource 4" space2
    ]

    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space1, 0, 10)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Ok(ResourcesResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.All(pagedResult.Items, fun item -> Assert.Equal(space1, item.SpaceId))
    | _ -> Assert.Fail("Expected ResourcesResult")
}

[<Fact>]
let ``GetAllResources with pagination returns correct subset`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()

    let resources = [
        createTestResource "Resource 1" space1
        createTestResource "Resource 2" space1
        createTestResource "Resource 3" space1
        createTestResource "Resource 4" space1
        createTestResource "Resource 5" space1
    ]

    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space1, 1, 2) // Skip 1, take 2

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Ok(ResourcesResult pagedResult) ->
        Assert.Equal(5, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.Equal(1, pagedResult.Skip)
        Assert.Equal(2, pagedResult.Take)
    | _ -> Assert.Fail("Expected ResourcesResult")
}

[<Fact>]
let ``GetAllResources returns empty list when no resources in space`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let space2 = SpaceId.NewId()
    let space3 = SpaceId.NewId() // No resources in this space

    let resources = [
        createTestResource "Resource 1" space1
        createTestResource "Resource 2" space2
    ]

    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space3, 0, 10)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Ok(ResourcesResult pagedResult) ->
        Assert.Equal(0, pagedResult.TotalCount)
        Assert.Empty(pagedResult.Items)
    | _ -> Assert.Fail("Expected ResourcesResult")
}

[<Fact>]
let ``CreateResource allows same name in different spaces`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let space2 = SpaceId.NewId()

    // Resource with name "API Resource" exists in space1
    let existingResource = createTestResource "API Resource" space1

    let resourceRepository =
        MockResourceRepository([ existingResource ]) :> IResourceRepository

    let appRepository = MockAppRepository() :> IAppRepository

    // Create a new resource with the same name but in space2
    let newResource = createTestResource "API Resource" space2
    let command = CreateResource(UserId.NewId(), newResource)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Ok(ResourceResult _) -> Assert.True(true) // Success - same name allowed in different space
    | Error error -> Assert.Fail($"Expected success but got error: {error}")
}

[<Fact>]
let ``CreateResource rejects duplicate name in same space`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()

    // Resource with name "API Resource" exists in space1
    let existingResource = createTestResource "API Resource" space1
    let mockRepo = MockResourceRepository([ existingResource ])
    let resourceRepository = mockRepo :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository

    // Try to create another resource with the same name in the same space
    let newResource = createTestResource "API Resource" space1
    let command = CreateResource(UserId.NewId(), newResource)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Error(Conflict msg) -> Assert.Contains("already exists", msg)
    | Ok _ -> Assert.Fail("Expected Conflict error for duplicate name in same space")
    | Error error -> Assert.Fail($"Expected Conflict error but got: {error}")
}

[<Fact>]
let ``GetAllResources with negative skip returns validation error`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let resources = [ createTestResource "Resource 1" space1 ]
    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space1, -1, 10)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Skip cannot be negative", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAllResources with take less than or equal to 0 returns validation error`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let resources = [ createTestResource "Resource 1" space1 ]
    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space1, 0, 0)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Take must be between 1 and 100", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAllResources with take greater than 100 returns validation error`` () = task {
    // Arrange
    let space1 = SpaceId.NewId()
    let resources = [ createTestResource "Resource 1" space1 ]
    let resourceRepository = MockResourceRepository(resources) :> IResourceRepository
    let appRepository = MockAppRepository() :> IAppRepository
    let command = GetAllResources(space1, 0, 101)

    // Act
    let! result = ResourceHandler.handleCommand resourceRepository appRepository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Take must be between 1 and 100", message)
    | _ -> Assert.Fail("Expected ValidationError")
}