namespace Freetool.Application.Handlers

open System
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Application.Commands
open Freetool.Application.Mappers
open Freetool.Application.DTOs

module FolderHandler =

    let handleCommand
        (folderRepository: IFolderRepository)
        (command: FolderCommand)
        : Task<Result<FolderCommandResult, DomainError>> =
        task {
            match command with
            | CreateFolder(actorUserId, validatedFolder) ->
                // Check if a folder with the same name already exists in the parent
                let folderName =
                    FolderName.Create(Some(Folder.getName validatedFolder))
                    |> function
                        | Ok n -> n
                        | Error err ->
                            failwith $"Invariant violation: ValidatedFolder should have valid name, but got: {err}"

                let parentId = Folder.getParentId validatedFolder
                let! existsByNameInParent = folderRepository.ExistsByNameInParentAsync folderName parentId

                if existsByNameInParent then
                    return Error(Conflict "A folder with this name already exists in the parent directory")
                else
                    // If has parent, validate parent exists
                    match parentId with
                    | None ->
                        // Root folder - proceed with creation
                        match! folderRepository.AddAsync validatedFolder with
                        | Error error -> return Error error
                        | Ok() -> return Ok(FolderResult(validatedFolder.State))
                    | Some parentFolderId ->
                        // Check if parent exists
                        let! parentExists = folderRepository.ExistsAsync parentFolderId

                        if not parentExists then
                            return Error(NotFound "Parent folder not found")
                        else
                            match! folderRepository.AddAsync validatedFolder with
                            | Error error -> return Error error
                            | Ok() -> return Ok(FolderResult(validatedFolder.State))

            | DeleteFolder(actorUserId, folderId) ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderIdObj = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetByIdAsync folderIdObj

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found")
                    | Some folder ->
                        // Mark folder for deletion to create the delete event
                        let folderWithDeleteEvent = Folder.markForDeletion actorUserId folder

                        // Delete folder and save event atomically
                        match! folderRepository.DeleteAsync folderWithDeleteEvent with
                        | Error error -> return Error error
                        | Ok() -> return Ok(FolderUnitResult())

            | UpdateFolderName(actorUserId, folderId, dto) ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderIdObj = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetByIdAsync folderIdObj

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found")
                    | Some folder ->
                        // Check if the new name already exists in the same parent (only if it's different from current name)
                        if Folder.getName folder <> dto.Name then
                            match FolderName.Create(Some dto.Name) with
                            | Error error -> return Error error
                            | Ok newFolderName ->
                                let parentId = Folder.getParentId folder

                                let! existsByNameInParent =
                                    folderRepository.ExistsByNameInParentAsync newFolderName parentId

                                if existsByNameInParent then
                                    return
                                        Error(Conflict "A folder with this name already exists in the parent directory")
                                else
                                    match FolderMapper.fromUpdateNameDto actorUserId dto folder with
                                    | Error error -> return Error error
                                    | Ok validatedFolder ->
                                        match! folderRepository.UpdateAsync validatedFolder with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(FolderResult(validatedFolder.State))
                        else
                            // Name hasn't changed, just update normally
                            match FolderMapper.fromUpdateNameDto actorUserId dto folder with
                            | Error error -> return Error error
                            | Ok validatedFolder ->
                                match! folderRepository.UpdateAsync validatedFolder with
                                | Error error -> return Error error
                                | Ok() -> return Ok(FolderResult(validatedFolder.State))

            | MoveFolder(actorUserId, folderId, dto) ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderIdObj = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetByIdAsync folderIdObj

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found")
                    | Some folder ->
                        // Parse and validate parent ID format first
                        let parseResult =
                            match dto.ParentId with
                            | None -> Ok(None)
                            | Some RootFolder -> Ok(None)
                            | Some(ChildFolder parentId) ->
                                match Guid.TryParse(parentId) with
                                | true, parentGuid -> Ok(Some(FolderId.FromGuid(parentGuid)))
                                | false, _ -> Error(ValidationError "Invalid parent folder ID format")

                        match parseResult with
                        | Error err -> return Error err
                        | Ok newParentId ->
                            // Validate new parent exists (if specified)
                            match newParentId with
                            | Some parentId ->
                                let! parentExists = folderRepository.ExistsAsync parentId

                                if not parentExists then
                                    return Error(NotFound "Parent folder not found")
                                // Check if moving to a child of itself (circular reference)
                                elif parentId = folderIdObj then
                                    return Error(ValidationError "Cannot move folder to itself")
                                else
                                    // Check if folder name conflicts in new parent
                                    let folderName =
                                        FolderName.Create(Some(Folder.getName folder))
                                        |> function
                                            | Ok n -> n
                                            | Error err ->
                                                failwith
                                                    $"Invariant violation: Folder from repository should have valid name, but got: {err}"

                                    let! existsByNameInParent =
                                        folderRepository.ExistsByNameInParentAsync folderName newParentId

                                    if existsByNameInParent then
                                        return
                                            Error(
                                                Conflict
                                                    "A folder with this name already exists in the target parent directory"
                                            )
                                    else
                                        let movedFolder = FolderMapper.fromMoveDto actorUserId dto folder

                                        match! folderRepository.UpdateAsync movedFolder with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(FolderResult(movedFolder.State))
                            | None ->
                                // Moving to root - check if name conflicts with other root folders
                                let folderName =
                                    FolderName.Create(Some(Folder.getName folder))
                                    |> function
                                        | Ok n -> n
                                        | Error err ->
                                            failwith
                                                $"Invariant violation: Folder from repository should have valid name, but got: {err}"

                                let! existsByNameInParent = folderRepository.ExistsByNameInParentAsync folderName None

                                if existsByNameInParent then
                                    return
                                        Error(Conflict "A folder with this name already exists in the root directory")
                                else
                                    let movedFolder = FolderMapper.fromMoveDto actorUserId dto folder

                                    match! folderRepository.UpdateAsync movedFolder with
                                    | Error error -> return Error error
                                    | Ok() -> return Ok(FolderResult(movedFolder.State))

            | GetFolderById folderId ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderIdObj = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetByIdAsync folderIdObj

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found")
                    | Some folder -> return Ok(FolderResult(folder.State))

            | GetFolderWithChildren folderId ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderIdObj = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetByIdAsync folderIdObj

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found")
                    | Some folder ->
                        let! children = folderRepository.GetChildrenAsync folderIdObj
                        return Ok(FolderResult(FolderMapper.toDataWithChildren folder children))

            | GetRootFolders(skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                else
                    let! folders = folderRepository.GetRootFoldersAsync skip take
                    let! totalCount = folderRepository.GetRootCountAsync()

                    let result = {
                        Items = folders |> List.map (fun folder -> folder.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(FoldersResult result)

            | GetAllFolders(spaceId, skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                else
                    match spaceId with
                    | None ->
                        // No space filter - return all folders (backward compatibility)
                        let! folders = folderRepository.GetAllAsync skip take
                        let! totalCount = folderRepository.GetCountAsync()

                        let result = {
                            Items = folders |> List.map (fun folder -> folder.State)
                            TotalCount = totalCount
                            Skip = skip
                            Take = take
                        }

                        return Ok(FoldersResult result)
                    | Some sId ->
                        // Filter by space
                        let! folders = folderRepository.GetBySpaceAsync sId skip take
                        let! totalCount = folderRepository.GetCountBySpaceAsync sId

                        let result = {
                            Items = folders |> List.map (fun folder -> folder.State)
                            TotalCount = totalCount
                            Skip = skip
                            Take = take
                        }

                        return Ok(FoldersResult result)

            | GetFoldersBySpaceIds(spaceIds, skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                elif List.isEmpty spaceIds then
                    let result = {
                        Items = []
                        TotalCount = 0
                        Skip = skip
                        Take = take
                    }

                    return Ok(FoldersResult result)
                else
                    let! folders = folderRepository.GetBySpaceIdsAsync spaceIds skip take
                    let! totalCount = folderRepository.GetCountBySpaceIdsAsync spaceIds

                    let result = {
                        Items = folders |> List.map (fun folder -> folder.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(FoldersResult result)
        }

type FolderHandler(folderRepository: IFolderRepository) =
    interface ICommandHandler<FolderCommand, FolderCommandResult> with
        member this.HandleCommand command =
            FolderHandler.handleCommand folderRepository command