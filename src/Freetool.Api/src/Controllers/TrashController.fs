namespace Freetool.Api.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.Commands
open Freetool.Application.DTOs
open Freetool.Application.Interfaces

[<ApiController>]
[<Route("trash")>]
type TrashController
    (
        appRepository: IAppRepository,
        folderRepository: IFolderRepository,
        resourceRepository: IResourceRepository,
        authorizationService: IAuthorizationService,
        commandHandler: ICommandHandler<TrashCommand, TrashCommandResult>
    ) =
    inherit AuthenticatedControllerBase()

    member private this.CheckAuthorization
        (spaceId: SpaceId)
        (permission: AuthRelation)
        : Task<Result<unit, DomainError>> =
        task {
            let userId = this.CurrentUserId

            let! hasPermission =
                authorizationService.CheckPermissionAsync
                    (User(userId.Value.ToString()))
                    permission
                    (SpaceObject(spaceId.Value.ToString()))

            if hasPermission then
                return Ok()
            else
                let permissionName = AuthTypes.relationToString permission
                return Error(InvalidOperation $"User does not have {permissionName} permission on this space")
        }

    member private this.GetSpaceIdFromFolder(folderId: FolderId) : Task<Result<SpaceId, DomainError>> = task {
        let! folderOption = folderRepository.GetByIdAsync folderId

        match folderOption with
        | None ->
            let! deletedFolderOption = folderRepository.GetDeletedByIdAsync folderId

            match deletedFolderOption with
            | None -> return Error(NotFound "Folder not found")
            | Some folder -> return Ok(Folder.getSpaceId folder)
        | Some folder -> return Ok(Folder.getSpaceId folder)
    }

    [<HttpGet("space/{spaceId}")>]
    [<ProducesResponseType(typeof<PagedResult<TrashItemDto>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetTrashBySpace
        (spaceId: string, [<FromQuery>] skip: int, [<FromQuery>] take: int)
        : Task<IActionResult> =
        task {
            let skipValue = if skip < 0 then 0 else skip

            let takeValue =
                if take <= 0 then 50
                elif take > 100 then 100
                else take

            let! result = commandHandler.HandleCommand(GetTrashBySpace(spaceId, skipValue, takeValue))

            return
                match result with
                | Ok(TrashListResult trashList) -> this.Ok(trashList) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
        }

    [<HttpPost("app/{appId}/restore")>]
    [<ProducesResponseType(typeof<RestoreResultDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RestoreApp(appId: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        match Guid.TryParse appId with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid app ID format")
        | true, guid ->
            let appIdObj = AppId.FromGuid guid
            let! appOption = appRepository.GetDeletedByIdAsync appIdObj

            match appOption with
            | None -> return this.HandleDomainError(NotFound "App not found in trash")
            | Some app ->
                let folderId = App.getFolderId app
                let! spaceIdResult = this.GetSpaceIdFromFolder folderId

                match spaceIdResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok spaceId ->
                    let! authResult = this.CheckAuthorization spaceId AppDelete

                    match authResult with
                    | Error error -> return this.HandleDomainError(error)
                    | Ok() ->
                        let! result = commandHandler.HandleCommand(RestoreApp(userId, appId))

                        return
                            match result with
                            | Ok(RestoreAppResult restoreResult) -> this.Ok(restoreResult) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
    }

    [<HttpPost("folder/{folderId}/restore")>]
    [<ProducesResponseType(typeof<RestoreResultDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RestoreFolder(folderId: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        match Guid.TryParse folderId with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
        | true, guid ->
            let folderIdObj = FolderId.FromGuid guid
            let! folderOption = folderRepository.GetDeletedByIdAsync folderIdObj

            match folderOption with
            | None -> return this.HandleDomainError(NotFound "Folder not found in trash")
            | Some folder ->
                let spaceId = Folder.getSpaceId folder
                let! authResult = this.CheckAuthorization spaceId FolderDelete

                match authResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok() ->
                    let! result = commandHandler.HandleCommand(RestoreFolder(userId, folderId))

                    return
                        match result with
                        | Ok(RestoreFolderResult restoreResult) -> this.Ok(restoreResult) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpPost("resource/{resourceId}/restore")>]
    [<ProducesResponseType(typeof<RestoreResultDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RestoreResource(resourceId: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        match Guid.TryParse resourceId with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
        | true, guid ->
            let resourceIdObj = ResourceId.FromGuid guid
            let! resourceOption = resourceRepository.GetDeletedByIdAsync resourceIdObj

            match resourceOption with
            | None -> return this.HandleDomainError(NotFound "Resource not found in trash")
            | Some resource ->
                let spaceId = Resource.getSpaceId resource
                let! authResult = this.CheckAuthorization spaceId ResourceDelete

                match authResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok() ->
                    let! result = commandHandler.HandleCommand(RestoreResource(userId, resourceId))

                    return
                        match result with
                        | Ok(RestoreResourceResult restoreResult) -> this.Ok(restoreResult) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    member private this.HandleDomainError(error: DomainError) : IActionResult =
        match error with
        | ValidationError message ->
            this.BadRequest {|
                error = "Validation failed"
                message = message
            |}
            :> IActionResult
        | NotFound message ->
            this.NotFound {|
                error = "Resource not found"
                message = message
            |}
            :> IActionResult
        | Conflict message ->
            this.Conflict {|
                error = "Conflict"
                message = message
            |}
            :> IActionResult
        | InvalidOperation message ->
            this.UnprocessableEntity {|
                error = "Invalid operation"
                message = message
            |}
            :> IActionResult