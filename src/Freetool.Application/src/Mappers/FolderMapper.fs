namespace Freetool.Application.Mappers

open System
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

module FolderMapper =

    let fromCreateDto (actorUserId: UserId) (dto: CreateFolderDto) : Result<ValidatedFolder, DomainError> =
        let parentId =
            match dto.Location with
            | None -> None
            | Some RootFolder -> None
            | Some(ChildFolder parentId) ->
                match Guid.TryParse(parentId) with
                | true, guid -> Some(FolderId.FromGuid(guid))
                | false, _ -> None // Will be validated in handler

        let spaceId =
            match Guid.TryParse(dto.SpaceId) with
            | true, guid -> SpaceId.FromGuid(guid)
            | false, _ -> SpaceId.NewId() // This will fail validation later if invalid

        Folder.create actorUserId dto.Name parentId spaceId

    let fromUpdateNameDto
        (actorUserId: UserId)
        (dto: UpdateFolderNameDto)
        (folder: ValidatedFolder)
        : Result<ValidatedFolder, DomainError> =
        Folder.updateName actorUserId dto.Name folder

    let fromMoveDto (actorUserId: UserId) (dto: MoveFolderDto) (folder: ValidatedFolder) : ValidatedFolder =
        let parentId =
            match dto.ParentId with
            | None -> None
            | Some RootFolder -> None
            | Some(ChildFolder parentId) ->
                match Guid.TryParse(parentId) with
                | true, guid -> Some(FolderId.FromGuid(guid))
                | false, _ -> None // Will be validated in handler

        Folder.moveToParent actorUserId parentId folder

    let toDataWithChildren (folder: ValidatedFolder) (children: ValidatedFolder list) : FolderData =
        // Populate the children field with the provided children
        let folderDataWithChildren = folder.State
        folderDataWithChildren.Children <- children |> List.map (fun child -> child.State)
        folderDataWithChildren