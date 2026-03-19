module Freetool.Application.Tests.FolderHandlerTests

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
type MockFolderRepository(folders: ValidatedFolder list) =
    let mutable folderList = folders

    interface IFolderRepository with
        member _.GetByIdAsync(folderId: FolderId) : Task<ValidatedFolder option> = task {
            return folderList |> List.tryFind (fun f -> f.State.Id = folderId)
        }

        member _.GetChildrenAsync(folderId: FolderId) : Task<ValidatedFolder list> = task {
            return folderList |> List.filter (fun f -> f.State.ParentId = Some(folderId))
        }

        member _.GetRootFoldersAsync (skip: int) (take: int) : Task<ValidatedFolder list> = task {
            return
                folderList
                |> List.filter (fun f -> f.State.ParentId.IsNone)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedFolder list> = task {
            return folderList |> List.skip skip |> List.truncate take
        }

        member _.GetBySpaceAsync (spaceId: SpaceId) (skip: int) (take: int) : Task<ValidatedFolder list> = task {
            return
                folderList
                |> List.filter (fun f -> f.State.SpaceId = spaceId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.AddAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folder :: folderList
            return Ok()
        }

        member _.UpdateAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folder :: (folderList |> List.filter (fun f -> f.State.Id <> folder.State.Id))
            return Ok()
        }

        member _.DeleteAsync(folder: ValidatedFolder) : Task<Result<unit, DomainError>> = task {
            folderList <- folderList |> List.filter (fun f -> f.State.Id <> folder.State.Id)
            return Ok()
        }

        member _.ExistsAsync(folderId: FolderId) : Task<bool> = task {
            return folderList |> List.exists (fun f -> f.State.Id = folderId)
        }

        member _.ExistsByNameInParentAsync (folderName: FolderName) (parentId: FolderId option) : Task<bool> = task {
            return
                folderList
                |> List.exists (fun f -> f.State.Name = folderName && f.State.ParentId = parentId)
        }

        member _.GetCountAsync() : Task<int> = task { return folderList.Length }

        member _.GetCountBySpaceAsync(spaceId: SpaceId) : Task<int> = task {
            return folderList |> List.filter (fun f -> f.State.SpaceId = spaceId) |> List.length
        }

        member _.GetBySpaceIdsAsync (spaceIds: SpaceId list) (skip: int) (take: int) : Task<ValidatedFolder list> = task {
            return
                folderList
                |> List.filter (fun f -> spaceIds |> List.contains f.State.SpaceId)
                |> List.skip skip
                |> List.truncate take
        }

        member _.GetCountBySpaceIdsAsync(spaceIds: SpaceId list) : Task<int> = task {
            return
                folderList
                |> List.filter (fun f -> spaceIds |> List.contains f.State.SpaceId)
                |> List.length
        }

        member _.GetRootCountAsync() : Task<int> = task {
            return folderList |> List.filter (fun f -> f.State.ParentId.IsNone) |> List.length
        }

        member _.GetChildCountAsync(parentId: FolderId) : Task<int> = task {
            return
                folderList
                |> List.filter (fun f -> f.State.ParentId = Some(parentId))
                |> List.length
        }

        member _.GetDeletedBySpaceAsync(_spaceId: SpaceId) : Task<ValidatedFolder list> = task { return [] }

        member _.GetDeletedByIdAsync(_folderId: FolderId) : Task<ValidatedFolder option> = task { return None }

        member _.RestoreWithChildrenAsync(_folder: ValidatedFolder) : Task<Result<int, DomainError>> = task {
            return Ok 0
        }

        member _.CheckNameConflictAsync
            (_name: FolderName)
            (_parentId: FolderId option)
            (_spaceId: SpaceId)
            : Task<bool> =
            task { return false }

// Test helper to create a folder
let createTestFolder (name: string) (spaceId: SpaceId) (parentId: FolderId option) : ValidatedFolder =
    match Folder.create (UserId.NewId()) name parentId spaceId with
    | Ok folder -> folder
    | Error _ -> failwith "Failed to create test folder"

[<Fact>]
let ``GetAllFolders without workspace filter returns all folders`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let workspace2 = SpaceId.NewId()

    let folders = [
        createTestFolder "Folder 1" workspace1 None
        createTestFolder "Folder 2" workspace1 None
        createTestFolder "Folder 3" workspace2 None
        createTestFolder "Folder 4" workspace2 None
    ]

    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(None, 0, 10)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(FoldersResult pagedResult) ->
        Assert.Equal(4, pagedResult.TotalCount)
        Assert.Equal(4, pagedResult.Items.Length)
    | _ -> Assert.Fail("Expected FoldersResult")
}

[<Fact>]
let ``GetAllFolders with workspace filter returns only folders from that workspace`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let workspace2 = SpaceId.NewId()

    let folders = [
        createTestFolder "Folder 1" workspace1 None
        createTestFolder "Folder 2" workspace1 None
        createTestFolder "Folder 3" workspace2 None
        createTestFolder "Folder 4" workspace2 None
    ]

    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace1, 0, 10)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(FoldersResult pagedResult) ->
        Assert.Equal(2, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.All(pagedResult.Items, fun item -> Assert.Equal(workspace1, item.SpaceId))
    | _ -> Assert.Fail("Expected FoldersResult")
}

[<Fact>]
let ``GetAllFolders with workspace filter and pagination returns correct subset`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()

    let folders = [
        createTestFolder "Folder 1" workspace1 None
        createTestFolder "Folder 2" workspace1 None
        createTestFolder "Folder 3" workspace1 None
        createTestFolder "Folder 4" workspace1 None
        createTestFolder "Folder 5" workspace1 None
    ]

    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace1, 1, 2) // Skip 1, take 2

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(FoldersResult pagedResult) ->
        Assert.Equal(5, pagedResult.TotalCount)
        Assert.Equal(2, pagedResult.Items.Length)
        Assert.Equal(1, pagedResult.Skip)
        Assert.Equal(2, pagedResult.Take)
    | _ -> Assert.Fail("Expected FoldersResult")
}

[<Fact>]
let ``GetAllFolders with workspace filter returns empty list when no folders match`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let workspace2 = SpaceId.NewId()
    let workspace3 = SpaceId.NewId() // No folders in this workspace

    let folders = [
        createTestFolder "Folder 1" workspace1 None
        createTestFolder "Folder 2" workspace2 None
    ]

    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace3, 0, 10)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Ok(FoldersResult pagedResult) ->
        Assert.Equal(0, pagedResult.TotalCount)
        Assert.Empty(pagedResult.Items)
    | _ -> Assert.Fail("Expected FoldersResult")
}

[<Fact>]
let ``GetAllFolders with negative skip returns validation error`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let folders = [ createTestFolder "Folder 1" workspace1 None ]
    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace1, -1, 10)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Skip cannot be negative", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAllFolders with take less than or equal to 0 returns validation error`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let folders = [ createTestFolder "Folder 1" workspace1 None ]
    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace1, 0, 0)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Take must be between 1 and 100", message)
    | _ -> Assert.Fail("Expected ValidationError")
}

[<Fact>]
let ``GetAllFolders with take greater than 100 returns validation error`` () = task {
    // Arrange
    let workspace1 = SpaceId.NewId()
    let folders = [ createTestFolder "Folder 1" workspace1 None ]
    let repository = MockFolderRepository(folders) :> IFolderRepository
    let command = GetAllFolders(Some workspace1, 0, 101)

    // Act
    let! result = FolderHandler.handleCommand repository command

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Take must be between 1 and 100", message)
    | _ -> Assert.Fail("Expected ValidationError")
}