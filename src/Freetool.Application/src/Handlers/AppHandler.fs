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
open Freetool.Application.Services

module AppHandler =

    let handleCommand
        (appRepository: IAppRepository)
        (command: AppCommand)
        : Task<Result<AppCommandResult, DomainError>> =
        task {
            match command with
            | CreateApp(actorUserId, validatedApp) ->
                // Check if app name already exists in folder
                let appName =
                    AppName.Create(Some(App.getName validatedApp))
                    |> function
                        | Ok name -> name
                        | Error err ->
                            failwith $"Invariant violation: ValidatedApp should have valid name, but got: {err}"

                let folderId = App.getFolderId validatedApp

                let! existsByNameAndFolder = appRepository.ExistsByNameAndFolderIdAsync appName folderId

                if existsByNameAndFolder then
                    return Error(Conflict "An app with this name already exists in the folder")
                else
                    match! appRepository.AddAsync validatedApp with
                    | Error error -> return Error error
                    | Ok() -> return Ok(AppResult(validatedApp.State))

            | DeleteApp(actorUserId, appId) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    // Delete app directly - repository handles validation and event creation
                    match! appRepository.DeleteAsync appIdObj actorUserId with
                    | Error error -> return Error error
                    | Ok() -> return Ok(AppUnitResult())

            | UpdateAppName(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        // Check if new name conflicts with existing app in same folder (only if name changed)
                        if App.getName app <> dto.Name then
                            match AppName.Create(Some dto.Name) with
                            | Error error -> return Error error
                            | Ok newAppName ->
                                let folderId = App.getFolderId app

                                let! existsByNameAndFolder =
                                    appRepository.ExistsByNameAndFolderIdAsync newAppName folderId

                                if existsByNameAndFolder then
                                    return Error(Conflict "An app with this name already exists in the folder")
                                else
                                    // Update name using domain method (automatically creates event)
                                    match App.updateName actorUserId dto.Name app with
                                    | Error error -> return Error error
                                    | Ok updatedApp ->
                                        // Save app and events atomically
                                        match! appRepository.UpdateAsync updatedApp with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(AppResult(updatedApp.State))
                        else
                            return Ok(AppResult(app.State))

            | UpdateAppInputs(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        // Parse and validate inputs from DTO
                        match AppMapper.fromUpdateInputsDto dto app with
                        | Error error -> return Error error
                        | Ok unvalidatedApp ->
                            let inputs = unvalidatedApp.State.Inputs

                            // Update inputs using domain method (automatically creates event)
                            match App.updateInputs actorUserId inputs app with
                            | Error error -> return Error error
                            | Ok updatedApp ->
                                // Save app and events atomically
                                match! appRepository.UpdateAsync updatedApp with
                                | Error error -> return Error error
                                | Ok() -> return Ok(AppResult(updatedApp.State))

            | GetAppById appId ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app -> return Ok(AppResult(app.State))

            | GetAppsByFolderId(folderId, skip, take) ->
                match Guid.TryParse folderId with
                | false, _ -> return Error(ValidationError "Invalid folder ID format")
                | true, guid ->
                    if skip < 0 then
                        return Error(ValidationError "Skip cannot be negative")
                    elif take <= 0 || take > 100 then
                        return Error(ValidationError "Take must be between 1 and 100")
                    else
                        let folderIdObj = FolderId.FromGuid guid
                        let! apps = appRepository.GetByFolderIdAsync folderIdObj skip take
                        let! totalCount = appRepository.GetCountByFolderIdAsync folderIdObj

                        let result = {
                            Items = apps |> List.map (fun app -> app.State)
                            TotalCount = totalCount
                            Skip = skip
                            Take = take
                        }

                        return Ok(AppsResult result)

            | GetAllApps(skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                else
                    let! apps = appRepository.GetAllAsync skip take
                    let! totalCount = appRepository.GetCountAsync()

                    let result = {
                        Items = apps |> List.map (fun app -> app.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(AppsResult result)

            | GetAppsBySpaceIds(spaceIds, skip, take) ->
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

                    return Ok(AppsResult result)
                else
                    let! apps = appRepository.GetBySpaceIdsAsync spaceIds skip take
                    let! totalCount = appRepository.GetCountBySpaceIdsAsync spaceIds

                    let result = {
                        Items = apps |> List.map (fun app -> app.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(AppsResult result)

            | UpdateAppQueryParameters(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        return
                            Error(
                                InvalidOperation
                                    "Resource repository required for URL parameters update - use AppHandler.handleCommandWithResourceRepository"
                            )

            | UpdateAppBody(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        return
                            Error(
                                InvalidOperation
                                    "Resource repository required for body update - use AppHandler.handleCommandWithResourceRepository"
                            )

            | UpdateAppHeaders(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        return
                            Error(
                                InvalidOperation
                                    "Resource repository required for headers update - use AppHandler.handleCommandWithResourceRepository"
                            )

            | UpdateAppUrlPath(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        match App.updateUrlPath actorUserId dto.UrlPath app with
                        | Error error -> return Error error
                        | Ok updatedApp ->
                            match! appRepository.UpdateAsync updatedApp with
                            | Error error -> return Error error
                            | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppHttpMethod(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        match App.updateHttpMethod actorUserId dto.HttpMethod app with
                        | Error error -> return Error error
                        | Ok updatedApp ->
                            match! appRepository.UpdateAsync updatedApp with
                            | Error error -> return Error error
                            | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppUseDynamicJsonBody(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        match App.updateUseDynamicJsonBody actorUserId dto.UseDynamicJsonBody app with
                        | Error error -> return Error error
                        | Ok updatedApp ->
                            match! appRepository.UpdateAsync updatedApp with
                            | Error error -> return Error error
                            | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppSqlConfig(_, _, _) ->
                return
                    Error(
                        InvalidOperation
                            "Resource repository required for SQL config update - use AppHandler.handleCommandWithResourceRepository"
                    )

            | UpdateAppDescription(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        match App.updateDescription actorUserId dto.Description app with
                        | Error error -> return Error error
                        | Ok updatedApp ->
                            match! appRepository.UpdateAsync updatedApp with
                            | Error error -> return Error error
                            | Ok() -> return Ok(AppResult(updatedApp.State))
        }

    let handleCommandWithResourceRepository
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (command: AppCommand)
        : Task<Result<AppCommandResult, DomainError>> =
        task {
            match command with
            | UpdateAppQueryParameters(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        let! resourceOption = resourceRepository.GetByIdAsync app.State.ResourceId

                        match resourceOption with
                        | None -> return Error(NotFound "Resource not found")
                        | Some resource ->
                            let newUrlParameters = dto.UrlParameters |> List.map AppMapper.keyValuePairFromDto
                            let resourceConflictData = Resource.toConflictData resource

                            match App.updateUrlParameters actorUserId newUrlParameters resourceConflictData app with
                            | Error error -> return Error error
                            | Ok updatedApp ->
                                match! appRepository.UpdateAsync updatedApp with
                                | Error error -> return Error error
                                | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppBody(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        let! resourceOption = resourceRepository.GetByIdAsync app.State.ResourceId

                        match resourceOption with
                        | None -> return Error(NotFound "Resource not found")
                        | Some resource ->
                            let newBody = dto.Body |> List.map AppMapper.keyValuePairFromDto
                            let resourceConflictData = Resource.toConflictData resource

                            match App.updateBody actorUserId newBody resourceConflictData app with
                            | Error error -> return Error error
                            | Ok updatedApp ->
                                match! appRepository.UpdateAsync updatedApp with
                                | Error error -> return Error error
                                | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppHeaders(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        let! resourceOption = resourceRepository.GetByIdAsync app.State.ResourceId

                        match resourceOption with
                        | None -> return Error(NotFound "Resource not found")
                        | Some resource ->
                            let existingHeaders =
                                app.State.Headers |> List.map (fun header -> (header.Key, header.Value))

                            let incomingHeaders =
                                dto.Headers |> List.map (fun header -> (header.Key, header.Value))

                            let mergedHeaders =
                                AuthorizationHeaderRedaction.restoreRedactedAuthorizationHeader
                                    existingHeaders
                                    incomingHeaders

                            let mergedDto = {
                                dto with
                                    Headers = mergedHeaders |> List.map (fun (k, v) -> { Key = k; Value = v })
                            }

                            let newHeaders = mergedDto.Headers |> List.map AppMapper.keyValuePairFromDto
                            let resourceConflictData = Resource.toConflictData resource

                            match App.updateHeaders actorUserId newHeaders resourceConflictData app with
                            | Error error -> return Error error
                            | Ok updatedApp ->
                                match! appRepository.UpdateAsync updatedApp with
                                | Error error -> return Error error
                                | Ok() -> return Ok(AppResult(updatedApp.State))

            | UpdateAppSqlConfig(actorUserId, appId, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        let! resourceOption = resourceRepository.GetByIdAsync app.State.ResourceId

                        match resourceOption with
                        | None -> return Error(NotFound "Resource not found")
                        | Some resource ->
                            match Resource.getResourceKind resource with
                            | ResourceKind.Http ->
                                return Error(ValidationError "SQL config is only supported for SQL resources")
                            | ResourceKind.Sql ->
                                match AppMapper.fromUpdateSqlConfigDto dto with
                                | Error error -> return Error error
                                | Ok sqlConfig ->
                                    match App.updateSqlConfig actorUserId sqlConfig app with
                                    | Error error -> return Error error
                                    | Ok updatedApp ->
                                        match! appRepository.UpdateAsync updatedApp with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(AppResult(updatedApp.State))

            | _ -> return! handleCommand appRepository command
        }

type AppHandler(appRepository: IAppRepository) =
    interface ICommandHandler<AppCommand, AppCommandResult> with
        member this.HandleCommand command =
            AppHandler.handleCommand appRepository command