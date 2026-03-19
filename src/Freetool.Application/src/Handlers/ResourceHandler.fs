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

module ResourceHandler =

    let handleCommand
        (resourceRepository: IResourceRepository)
        (appRepository: IAppRepository)
        (command: ResourceCommand)
        : Task<Result<ResourceCommandResult, DomainError>> =
        task {
            match command with
            | CreateResource(actorUserId, validatedResource) ->
                // Check if name already exists within the same space
                let resourceName =
                    ResourceName.Create(Some(Resource.getName validatedResource))
                    |> function
                        | Ok n -> n
                        | Error err ->
                            failwith $"Invariant violation: ValidatedResource should have valid name, but got: {err}"

                let spaceId = Resource.getSpaceId validatedResource
                let! existsByName = resourceRepository.CheckNameConflictAsync resourceName spaceId

                if existsByName then
                    return Error(Conflict "A resource with this name already exists in this space")
                else
                    match! resourceRepository.AddAsync validatedResource with
                    | Error error -> return Error error
                    | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | DeleteResource(actorUserId, resourceId) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        // Mark resource for deletion to create the delete event
                        let resourceWithDeleteEvent = Resource.markForDeletion actorUserId resource

                        // Delete resource and save event atomically
                        match! resourceRepository.DeleteAsync resourceWithDeleteEvent with
                        | Error error -> return Error error
                        | Ok() -> return Ok(ResourceUnitResult())

            | UpdateResourceName(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        let spaceId = Resource.getSpaceId resource

                        // Check if the new name already exists (only if it's different from current name)
                        if Resource.getName resource <> dto.Name then
                            match ResourceName.Create(Some dto.Name) with
                            | Error error -> return Error error
                            | Ok newResourceName ->
                                let! existsByName = resourceRepository.CheckNameConflictAsync newResourceName spaceId

                                if existsByName then
                                    return Error(Conflict "A resource with this name already exists in this space")
                                else
                                    match ResourceMapper.fromUpdateNameDto actorUserId dto resource with
                                    | Error error -> return Error error
                                    | Ok validatedResource ->
                                        match! resourceRepository.UpdateAsync validatedResource with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(ResourceResult(validatedResource.State))
                        else
                            // Name hasn't changed, just update normally
                            match ResourceMapper.fromUpdateNameDto actorUserId dto resource with
                            | Error error -> return Error error
                            | Ok validatedResource ->
                                match! resourceRepository.UpdateAsync validatedResource with
                                | Error error -> return Error error
                                | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceDescription(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        match ResourceMapper.fromUpdateDescriptionDto actorUserId dto resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceBaseUrl(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        match ResourceMapper.fromUpdateBaseUrlDto actorUserId dto resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceUrlParameters(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        let! apps = appRepository.GetByResourceIdAsync resourceIdObj

                        let appConflictData =
                            apps
                            |> List.map (fun app -> {
                                AppId = (App.getId app).Value.ToString()
                                UrlParameters = App.getUrlParameters app
                                Headers = App.getHeaders app
                                Body = App.getBody app
                            })

                        match ResourceMapper.fromUpdateUrlParametersDto actorUserId dto appConflictData resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceHeaders(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        let! apps = appRepository.GetByResourceIdAsync resourceIdObj

                        let appConflictData =
                            apps
                            |> List.map (fun app -> {
                                AppId = (App.getId app).Value.ToString()
                                UrlParameters = App.getUrlParameters app
                                Headers = App.getHeaders app
                                Body = App.getBody app
                            })

                        let existingHeaders =
                            resource.State.Headers |> List.map (fun header -> (header.Key, header.Value))

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

                        match ResourceMapper.fromUpdateHeadersDto actorUserId mergedDto appConflictData resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceBody(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        let! apps = appRepository.GetByResourceIdAsync resourceIdObj

                        let appConflictData =
                            apps
                            |> List.map (fun app -> {
                                AppId = (App.getId app).Value.ToString()
                                UrlParameters = App.getUrlParameters app
                                Headers = App.getHeaders app
                                Body = App.getBody app
                            })

                        match ResourceMapper.fromUpdateBodyDto actorUserId dto appConflictData resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | UpdateResourceDatabaseConfig(actorUserId, resourceId, dto) ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource ->
                        match ResourceMapper.fromUpdateDatabaseConfigDto actorUserId dto resource with
                        | Error error -> return Error error
                        | Ok validatedResource ->
                            match! resourceRepository.UpdateAsync validatedResource with
                            | Error error -> return Error error
                            | Ok() -> return Ok(ResourceResult(validatedResource.State))

            | GetResourceById resourceId ->
                match Guid.TryParse resourceId with
                | false, _ -> return Error(ValidationError "Invalid resource ID format")
                | true, guid ->
                    let resourceIdObj = ResourceId.FromGuid guid
                    let! resourceOption = resourceRepository.GetByIdAsync resourceIdObj

                    match resourceOption with
                    | None -> return Error(NotFound "Resource not found")
                    | Some resource -> return Ok(ResourceResult(resource.State))

            | GetAllResources(spaceId, skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                else
                    let! resources = resourceRepository.GetBySpaceAsync spaceId skip take
                    let! totalCount = resourceRepository.GetCountBySpaceAsync spaceId

                    let result = {
                        Items = resources |> List.map (fun resource -> resource.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(ResourcesResult result)
        }

type ResourceHandler(resourceRepository: IResourceRepository, appRepository: IAppRepository) =
    interface ICommandHandler<ResourceCommand, ResourceCommandResult> with
        member this.HandleCommand command =
            ResourceHandler.handleCommand resourceRepository appRepository command