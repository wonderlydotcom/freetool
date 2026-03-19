namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type FolderCommandResult =
    | FolderResult of FolderData
    | FoldersResult of PagedResult<FolderData>
    | FolderUnitResult of unit

type FolderCommand =
    | CreateFolder of actorUserId: UserId * ValidatedFolder
    | GetFolderById of folderId: string
    | GetFolderWithChildren of folderId: string
    | GetRootFolders of skip: int * take: int
    | GetAllFolders of spaceId: SpaceId option * skip: int * take: int
    | GetFoldersBySpaceIds of spaceIds: SpaceId list * skip: int * take: int
    | DeleteFolder of actorUserId: UserId * folderId: string
    | UpdateFolderName of actorUserId: UserId * folderId: string * UpdateFolderNameDto
    | MoveFolder of actorUserId: UserId * folderId: string * MoveFolderDto