namespace Freetool.Api.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces
open Freetool.Application.Mappers

[<ApiController>]
[<Route("folder")>]
type FolderController
    (
        folderRepository: IFolderRepository,
        spaceRepository: ISpaceRepository,
        commandHandler: ICommandHandler<FolderCommand, FolderCommandResult>,
        authorizationService: IAuthorizationService
    ) =
    inherit AuthenticatedControllerBase()

    /// Checks if the current user is an organization administrator
    member private this.IsOrganizationAdmin() : Task<bool> = task {
        let userId = this.CurrentUserId

        return!
            authorizationService.CheckPermissionAsync
                (User(userId.ToString()))
                OrganizationAdmin
                (OrganizationObject "default")
    }

    /// Gets the list of space IDs that the current user has access to
    member private this.GetAccessibleSpaceIdsForCurrentUser() : Task<SpaceId list> = task {
        let! isOrgAdmin = this.IsOrganizationAdmin()

        if isOrgAdmin then
            // Org admins can see all spaces
            let! allSpaces = spaceRepository.GetAllAsync 0 Int32.MaxValue
            return allSpaces |> List.map (fun space -> space.State.Id)
        else
            // Regular users see only spaces they're members or moderators of
            let userId = this.CurrentUserId
            let! memberSpaces = spaceRepository.GetByUserIdAsync userId
            let! moderatorSpaces = spaceRepository.GetByModeratorUserIdAsync userId

            return
                (memberSpaces @ moderatorSpaces)
                |> List.distinctBy (fun s -> s.State.Id)
                |> List.map (fun s -> s.State.Id)
    }

    [<HttpPost>]
    [<ProducesResponseType(typeof<FolderData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.CreateFolder([<FromBody>] createDto: CreateFolderDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Parse and validate space ID
        match Guid.TryParse(createDto.SpaceId) with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid space ID format")
        | true, spaceGuid ->
            let spaceId = SpaceId.FromGuid(spaceGuid)

            // Check authorization: user must have create_folder permission on space
            // Org admins inherit this permission via OpenFGA tupleToUserset
            let! hasPermission =
                authorizationService.CheckPermissionAsync
                    (User(userId.ToString()))
                    FolderCreate
                    (SpaceObject(spaceId.ToString()))

            if not hasPermission then
                return
                    this.StatusCode(
                        403,
                        {|
                            error = "Forbidden"
                            message = "You do not have permission to create folders in this space"
                        |}
                    )
                    :> IActionResult
            else
                match FolderMapper.fromCreateDto userId createDto with
                | Error domainError -> return this.HandleDomainError(domainError)
                | Ok validatedFolder ->
                    let! result = commandHandler.HandleCommand(CreateFolder(userId, validatedFolder))

                    return
                        match result with
                        | Ok(FolderResult folderDto) ->
                            this.CreatedAtAction(nameof this.GetFolderById, {| id = folderDto.Id |}, folderDto)
                            :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}")>]
    [<ProducesResponseType(typeof<FolderData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetFolderById(id: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetFolderById id)

        return
            match result with
            | Ok(FolderResult folderDto) -> this.Ok(folderDto) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}/children")>]
    [<ProducesResponseType(typeof<FolderData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetFolderWithChildren(id: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetFolderWithChildren id)

        return
            match result with
            | Ok(FolderResult folderWithChildrenData) -> this.Ok(folderWithChildrenData) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("root")>]
    [<ProducesResponseType(typeof<PagedResult<FolderData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetRootFolders([<FromQuery>] skip: int, [<FromQuery>] take: int) : Task<IActionResult> = task {
        let skipValue = if skip < 0 then 0 else skip

        let takeValue =
            if take <= 0 then 50
            elif take > 100 then 100
            else take

        let! result = commandHandler.HandleCommand(GetRootFolders(skipValue, takeValue))

        return
            match result with
            | Ok(FoldersResult pagedFolders) -> this.Ok(pagedFolders) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet>]
    [<ProducesResponseType(typeof<PagedResult<FolderData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetAllFolders
        ([<FromQuery>] spaceId: string, [<FromQuery>] skip: int, [<FromQuery>] take: int)
        : Task<IActionResult> =
        task {
            let skipValue = if skip < 0 then 0 else skip

            let takeValue =
                if take <= 0 then 50
                elif take > 100 then 100
                else take

            // Parse optional spaceId
            if String.IsNullOrWhiteSpace(spaceId) then
                // No space filter - filter by user's accessible spaces
                let! accessibleSpaceIds = this.GetAccessibleSpaceIdsForCurrentUser()

                if List.isEmpty accessibleSpaceIds then
                    let emptyResult: PagedResult<FolderData> = {
                        Items = []
                        TotalCount = 0
                        Skip = skipValue
                        Take = takeValue
                    }

                    return this.Ok(emptyResult) :> IActionResult
                else
                    let! result =
                        commandHandler.HandleCommand(GetFoldersBySpaceIds(accessibleSpaceIds, skipValue, takeValue))

                    return
                        match result with
                        | Ok(FoldersResult pagedFolders) -> this.Ok(pagedFolders) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
            else
                // Validate and parse space ID
                match Guid.TryParse(spaceId) with
                | false, _ -> return this.HandleDomainError(ValidationError "Invalid space ID format")
                | true, guid ->
                    let spaceIdObj = SpaceId.FromGuid(guid)

                    let! result = commandHandler.HandleCommand(GetAllFolders(Some spaceIdObj, skipValue, takeValue))

                    return
                        match result with
                        | Ok(FoldersResult pagedFolders) -> this.Ok(pagedFolders) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/name")>]
    [<ProducesResponseType(typeof<FolderData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateFolderName(id: string, [<FromBody>] updateDto: UpdateFolderNameDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Parse folder ID
        match Guid.TryParse(id) with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
        | true, folderGuid ->
            let folderId = FolderId.FromGuid(folderGuid)

            // Get folder to extract space ID
            let! folderOption = folderRepository.GetByIdAsync folderId

            match folderOption with
            | None -> return this.HandleDomainError(NotFound "Folder not found")
            | Some folder ->
                let spaceId = Folder.getSpaceId folder

                // Check authorization: user must have edit_folder permission on space
                // Org admins inherit this permission via OpenFGA tupleToUserset
                let! hasPermission =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        FolderEdit
                        (SpaceObject(spaceId.ToString()))

                if not hasPermission then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to edit folders in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(UpdateFolderName(userId, id, updateDto))

                    return
                        match result with
                        | Ok(FolderResult folderDto) -> this.Ok(folderDto) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/move")>]
    [<ProducesResponseType(typeof<FolderData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.MoveFolder(id: string, [<FromBody>] moveDto: MoveFolderDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Parse folder ID
        match Guid.TryParse(id) with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
        | true, folderGuid ->
            let folderId = FolderId.FromGuid(folderGuid)

            // Get folder to extract space ID
            let! folderOption = folderRepository.GetByIdAsync folderId

            match folderOption with
            | None -> return this.HandleDomainError(NotFound "Folder not found")
            | Some folder ->
                let spaceId = Folder.getSpaceId folder

                // Check authorization: user must have edit_folder permission on space
                // Org admins inherit this permission via OpenFGA tupleToUserset
                let! hasPermission =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        FolderEdit
                        (SpaceObject(spaceId.ToString()))

                if not hasPermission then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to edit folders in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(MoveFolder(userId, id, moveDto))

                    return
                        match result with
                        | Ok(FolderResult folderDto) -> this.Ok(folderDto) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpDelete("{id}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.DeleteFolder(id: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Parse folder ID
        match Guid.TryParse(id) with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
        | true, folderGuid ->
            let folderId = FolderId.FromGuid(folderGuid)

            // Get folder to extract space ID
            let! folderOption = folderRepository.GetByIdAsync folderId

            match folderOption with
            | None -> return this.HandleDomainError(NotFound "Folder not found")
            | Some folder ->
                let spaceId = Folder.getSpaceId folder

                // Check authorization: user must have delete_folder permission on space
                // Org admins inherit this permission via OpenFGA tupleToUserset
                let! hasPermission =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        FolderDelete
                        (SpaceObject(spaceId.ToString()))

                if not hasPermission then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to delete folders in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(DeleteFolder(userId, id))

                    return
                        match result with
                        | Ok(FolderUnitResult _) -> this.NoContent() :> IActionResult
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