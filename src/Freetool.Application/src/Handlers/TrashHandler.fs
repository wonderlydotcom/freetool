namespace Freetool.Application.Handlers

open System
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Application.Commands
open Freetool.Application.DTOs

module TrashHandler =

    // Helper to generate unique name with (Restored) suffix
    let rec private generateUniqueName
        (baseName: string)
        (suffix: int)
        (checkConflict: string -> Task<bool>)
        : Task<string> =
        task {
            let candidateName =
                if suffix = 1 then
                    $"{baseName} (Restored)"
                else
                    $"{baseName} (Restored {suffix})"

            let! hasConflict = checkConflict candidateName

            if hasConflict then
                return! generateUniqueName baseName (suffix + 1) checkConflict
            else
                return candidateName
        }

    let handleCommand
        (appRepository: IAppRepository)
        (folderRepository: IFolderRepository)
        (resourceRepository: IResourceRepository)
        (command: TrashCommand)
        : Task<Result<TrashCommandResult, DomainError>> =
        task {
            match command with
            | GetTrashBySpace(spaceIdStr, skip, take) ->
                match Guid.TryParse spaceIdStr with
                | false, _ -> return Error(ValidationError "Invalid space ID format")
                | true, guid ->
                    let spaceId = SpaceId.FromGuid guid

                    // Get deleted folders and resources for this space
                    let! deletedFolders = folderRepository.GetDeletedBySpaceAsync spaceId
                    let! deletedResources = resourceRepository.GetDeletedBySpaceAsync spaceId

                    // Get folder IDs to query apps
                    // We need both deleted and non-deleted folders because apps can be deleted from active folders
                    let deletedFolderIds = deletedFolders |> List.map (fun f -> Folder.getId f)

                    // Get non-deleted folders using pagination (get enough to cover most cases)
                    let! nonDeletedFolders = folderRepository.GetBySpaceAsync spaceId 0 1000
                    let nonDeletedFolderIds = nonDeletedFolders |> List.map (fun f -> Folder.getId f)
                    let allFolderIds = (deletedFolderIds @ nonDeletedFolderIds) |> List.distinct

                    let! deletedApps =
                        if List.isEmpty allFolderIds then
                            Task.FromResult([])
                        else
                            appRepository.GetDeletedByFolderIdsAsync allFolderIds

                    // Convert to DTOs
                    let appItems =
                        deletedApps
                        |> List.map (fun app -> {
                            Id = (App.getId app).Value.ToString()
                            Name = App.getName app
                            ItemType = "app"
                            SpaceId = spaceIdStr
                            DeletedAt = app.State.UpdatedAt
                        })

                    let folderItems =
                        deletedFolders
                        |> List.map (fun folder -> {
                            Id = (Folder.getId folder).Value.ToString()
                            Name = Folder.getName folder
                            ItemType = "folder"
                            SpaceId = spaceIdStr
                            DeletedAt = folder.State.UpdatedAt
                        })

                    let resourceItems =
                        deletedResources
                        |> List.map (fun resource -> {
                            Id = (Resource.getId resource).Value.ToString()
                            Name = Resource.getName resource
                            ItemType = "resource"
                            SpaceId = spaceIdStr
                            DeletedAt = resource.State.UpdatedAt
                        })

                    let allItems =
                        appItems @ folderItems @ resourceItems
                        |> List.sortByDescending (fun item -> item.DeletedAt)

                    let totalCount = List.length allItems

                    let pagedItems = allItems |> List.skip skip |> List.truncate take

                    let result = {
                        Items = pagedItems
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(TrashListResult result)

            | RestoreApp(actorUserId, appIdStr) ->
                match Guid.TryParse appIdStr with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appId = AppId.FromGuid guid
                    let! appOption = appRepository.GetDeletedByIdAsync appId

                    match appOption with
                    | None -> return Error(NotFound "App not found in trash")
                    | Some app ->
                        // Check if resource needs to be restored (auto-restore deleted resource)
                        let resourceId = App.getResourceId app
                        let! resourceOption = resourceRepository.GetByIdAsync resourceId

                        let! autoRestoredResourceId = task {
                            match resourceOption with
                            | Some _ -> return None // Resource exists, no auto-restore needed
                            | None ->
                                // Try to get the deleted resource
                                let! deletedResource = resourceRepository.GetDeletedByIdAsync resourceId

                                match deletedResource with
                                | None -> return None // Resource doesn't exist at all
                                | Some resource ->
                                    // Auto-restore the resource (without name change)
                                    let restoredResource = Resource.restore actorUserId None resource

                                    match! resourceRepository.RestoreAsync restoredResource with
                                    | Ok() -> return Some(resourceId.Value.ToString())
                                    | Error _ -> return None
                        }

                        // Check for name conflict
                        let appName = App.getName app
                        let folderId = App.getFolderId app
                        let! hasConflict = appRepository.CheckNameConflictAsync appName folderId

                        let! newName = task {
                            if hasConflict then
                                let! uniqueName =
                                    generateUniqueName appName 1 (fun name ->
                                        appRepository.CheckNameConflictAsync name folderId)

                                return Some uniqueName
                            else
                                return None
                        }

                        // Restore the app with optional new name
                        let restoredApp = App.restore actorUserId newName app

                        match! appRepository.RestoreAsync restoredApp with
                        | Error error -> return Error error
                        | Ok() ->
                            let result = {
                                RestoredId = appIdStr
                                NewName = newName
                                AutoRestoredResourceId = autoRestoredResourceId
                            }

                            return Ok(RestoreAppResult result)

            | RestoreFolder(actorUserId, folderIdStr) ->
                match Guid.TryParse folderIdStr with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    let folderId = FolderId.FromGuid guid
                    let! folderOption = folderRepository.GetDeletedByIdAsync folderId

                    match folderOption with
                    | None -> return Error(NotFound "Folder not found in trash")
                    | Some folder ->
                        // Check for name conflict
                        let folderName = folder.State.Name
                        let parentId = Folder.getParentId folder
                        let spaceId = Folder.getSpaceId folder
                        let! hasConflict = folderRepository.CheckNameConflictAsync folderName parentId spaceId

                        let! newName = task {
                            if hasConflict then
                                let checkConflict name = task {
                                    match FolderName.Create(Some name) with
                                    | Ok fn -> return! folderRepository.CheckNameConflictAsync fn parentId spaceId
                                    | Error _ -> return true // Invalid name, treat as conflict
                                }

                                let! uniqueName = generateUniqueName (Folder.getName folder) 1 checkConflict
                                return Some uniqueName
                            else
                                return None
                        }

                        // Create the restored folder with optional new name
                        let newFolderName =
                            newName
                            |> Option.bind (fun n ->
                                match FolderName.Create(Some n) with
                                | Ok fn -> Some fn
                                | Error _ -> None)

                        // Restore the folder (creates the restore event)
                        let restoredFolder = Folder.restore actorUserId newFolderName folder

                        // Restore folder and all its children using cascade restore
                        // The RestoreWithChildrenAsync handles restoring the folder and all its descendants
                        match! folderRepository.RestoreWithChildrenAsync restoredFolder with
                        | Error error -> return Error error
                        | Ok _restoredCount ->
                            let result = {
                                RestoredId = folderIdStr
                                NewName = newName
                                AutoRestoredResourceId = None
                            }

                            return Ok(RestoreFolderResult result)

            | RestoreResource(actorUserId, resourceIdStr) ->
                match Guid.TryParse resourceIdStr with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceId = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetDeletedByIdAsync resourceId

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found in trash")
                    | Some resource ->
                        // Check for name conflict
                        let resourceName = resource.State.Name
                        let spaceId = Resource.getSpaceId resource
                        let! hasConflict = resourceRepository.CheckNameConflictAsync resourceName spaceId

                        let! newName = task {
                            if hasConflict then
                                let checkConflict name = task {
                                    match ResourceName.Create(Some name) with
                                    | Ok rn -> return! resourceRepository.CheckNameConflictAsync rn spaceId
                                    | Error _ -> return true // Invalid name, treat as conflict
                                }

                                let! uniqueName = generateUniqueName (Resource.getName resource) 1 checkConflict

                                match ResourceName.Create(Some uniqueName) with
                                | Ok _ -> return Some uniqueName
                                | Error _ -> return None // Fallback, shouldn't happen
                            else
                                return None
                        }

                        // Create the new ResourceName value object if name changed
                        let newResourceName =
                            newName
                            |> Option.bind (fun n ->
                                match ResourceName.Create(Some n) with
                                | Ok rn -> Some rn
                                | Error _ -> None)

                        // Restore the resource with optional new name
                        let restoredResource = Resource.restore actorUserId newResourceName resource

                        match! resourceRepository.RestoreAsync restoredResource with
                        | Error error -> return Error error
                        | Ok() ->
                            let result = {
                                RestoredId = resourceIdStr
                                NewName = newName
                                AutoRestoredResourceId = None
                            }

                            return Ok(RestoreResourceResult result)
        }

type TrashHandler
    (appRepository: IAppRepository, folderRepository: IFolderRepository, resourceRepository: IResourceRepository) =
    interface ICommandHandler<TrashCommand, TrashCommandResult> with
        member this.HandleCommand command =
            TrashHandler.handleCommand appRepository folderRepository resourceRepository command