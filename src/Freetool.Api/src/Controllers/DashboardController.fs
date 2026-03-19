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

[<ApiController>]
[<Route("dashboard")>]
type DashboardController
    (
        dashboardRepository: IDashboardRepository,
        folderRepository: IFolderRepository,
        spaceRepository: ISpaceRepository,
        userRepository: IUserRepository,
        authorizationService: IAuthorizationService,
        commandHandler: ICommandHandler<DashboardCommand, DashboardCommandResult>
    ) =
    inherit AuthenticatedControllerBase()

    member private this.GetSpaceIdFromFolder(folderId: FolderId) : Task<Result<SpaceId, DomainError>> = task {
        let! folderOption = folderRepository.GetByIdAsync folderId

        match folderOption with
        | None -> return Error(NotFound "Folder not found")
        | Some folder -> return Ok(Folder.getSpaceId folder)
    }

    member private this.GetSpaceIdFromDashboard(dashboardId: string) : Task<Result<SpaceId, DomainError>> = task {
        match Guid.TryParse dashboardId with
        | false, _ -> return Error(ValidationError "Invalid dashboard ID format")
        | true, guid ->
            let dashboardIdObj = DashboardId.FromGuid guid
            let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

            match dashboardOption with
            | None -> return Error(NotFound "Dashboard not found")
            | Some dashboard ->
                let folderId = Dashboard.getFolderId dashboard
                return! this.GetSpaceIdFromFolder folderId
    }

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

    member private this.CheckRuntimeAuthorization(spaceId: SpaceId) : Task<Result<unit, DomainError>> = task {
        let! dashboardRunAuth = this.CheckAuthorization spaceId DashboardRun

        match dashboardRunAuth with
        | Error err -> return Error err
        | Ok() ->
            let! appRunAuth = this.CheckAuthorization spaceId AppRun

            match appRunAuth with
            | Error err -> return Error err
            | Ok() -> return Ok()
    }

    member private this.IsOrganizationAdmin() : Task<bool> = task {
        let userId = this.CurrentUserId

        return!
            authorizationService.CheckPermissionAsync
                (User(userId.Value.ToString()))
                OrganizationAdmin
                (OrganizationObject "default")
    }

    member private this.GetAccessibleSpaceIdsForCurrentUser() : Task<SpaceId list> = task {
        let! isOrgAdmin = this.IsOrganizationAdmin()

        if isOrgAdmin then
            let! allSpaces = spaceRepository.GetAllAsync 0 Int32.MaxValue
            return allSpaces |> List.map (fun space -> space.State.Id)
        else
            let userId = this.CurrentUserId
            let! memberSpaces = spaceRepository.GetByUserIdAsync userId
            let! moderatorSpaces = spaceRepository.GetByModeratorUserIdAsync userId

            return
                (memberSpaces @ moderatorSpaces)
                |> List.distinctBy (fun space -> space.State.Id)
                |> List.map (fun space -> space.State.Id)
    }

    member private this.HandleAuthorizationError() : IActionResult =
        this.StatusCode(
            403,
            {|
                error = "Forbidden"
                message = "You do not have permission to perform this action"
            |}
        )
        :> IActionResult

    member private this.GetCurrentUser() : Task<Result<CurrentUser, DomainError>> = task {
        let userId = this.CurrentUserId
        let! userOption = userRepository.GetByIdAsync userId

        match userOption with
        | None -> return Error(NotFound "User not found")
        | Some user ->
            return
                Ok {
                    Id = userId.Value.ToString()
                    Email = User.getEmail user
                    FirstName = User.getFirstName user
                    LastName = User.getLastName user
                }
    }

    [<HttpPost>]
    [<ProducesResponseType(typeof<DashboardData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.CreateDashboard([<FromBody>] createDto: CreateDashboardDto) : Task<IActionResult> = task {
        match Guid.TryParse createDto.FolderId with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
        | true, folderGuid ->
            let folderId = FolderId.FromGuid folderGuid

            let! spaceIdResult = this.GetSpaceIdFromFolder folderId

            match spaceIdResult with
            | Error error -> return this.HandleDomainError(error)
            | Ok spaceId ->
                let! authResult = this.CheckAuthorization spaceId DashboardCreate

                match authResult with
                | Error _ -> return this.HandleAuthorizationError()
                | Ok() ->
                    let! result = commandHandler.HandleCommand(CreateDashboard(this.CurrentUserId, createDto))

                    return
                        match result with
                        | Ok(DashboardResult dashboardDto) ->
                            this.CreatedAtAction(nameof this.GetDashboardById, {| id = dashboardDto.Id |}, dashboardDto)
                            :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}")>]
    [<ProducesResponseType(typeof<DashboardData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetDashboardById(id: string) : Task<IActionResult> = task {
        let! spaceIdResult = this.GetSpaceIdFromDashboard id

        match spaceIdResult with
        | Error error -> return this.HandleDomainError(error)
        | Ok spaceId ->
            let! authResult = this.CheckAuthorization spaceId DashboardRun

            match authResult with
            | Error _ -> return this.HandleAuthorizationError()
            | Ok() ->
                let! result = commandHandler.HandleCommand(GetDashboardById id)

                return
                    match result with
                    | Ok(DashboardResult dashboardDto) -> this.Ok(dashboardDto) :> IActionResult
                    | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                    | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("folder/{folderId}")>]
    [<ProducesResponseType(typeof<PagedResult<DashboardData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetDashboardsByFolderId
        (folderId: string, [<FromQuery>] skip: int, [<FromQuery>] take: int)
        : Task<IActionResult> =
        task {
            let skipValue = if skip < 0 then 0 else skip

            let takeValue =
                if take <= 0 then 50
                elif take > 100 then 100
                else take

            match Guid.TryParse folderId with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid folder ID format")
            | true, guid ->
                let folderIdObj = FolderId.FromGuid guid
                let! spaceIdResult = this.GetSpaceIdFromFolder folderIdObj

                match spaceIdResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok spaceId ->
                    let! authResult = this.CheckAuthorization spaceId DashboardRun

                    match authResult with
                    | Error _ -> return this.HandleAuthorizationError()
                    | Ok() ->
                        let! result =
                            commandHandler.HandleCommand(GetDashboardsByFolderId(folderId, skipValue, takeValue))

                        return
                            match result with
                            | Ok(DashboardsResult pagedDashboards) -> this.Ok(pagedDashboards) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpGet>]
    [<ProducesResponseType(typeof<PagedResult<DashboardData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetDashboards([<FromQuery>] skip: int, [<FromQuery>] take: int) : Task<IActionResult> = task {
        let skipValue = if skip < 0 then 0 else skip

        let takeValue =
            if take <= 0 then 50
            elif take > 100 then 100
            else take

        let! spaceIds = this.GetAccessibleSpaceIdsForCurrentUser()

        if List.isEmpty spaceIds then
            let emptyResult: PagedResult<DashboardData> = {
                Items = []
                TotalCount = 0
                Skip = skipValue
                Take = takeValue
            }

            return this.Ok(emptyResult) :> IActionResult
        else
            let! result = commandHandler.HandleCommand(GetDashboardsBySpaceIds(spaceIds, skipValue, takeValue))

            return
                match result with
                | Ok(DashboardsResult pagedDashboards) -> this.Ok(pagedDashboards) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/name")>]
    [<ProducesResponseType(typeof<DashboardData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateDashboardName(id: string, [<FromBody>] updateDto: UpdateDashboardNameDto) : Task<IActionResult> = task {
        let! spaceIdResult = this.GetSpaceIdFromDashboard id

        match spaceIdResult with
        | Error error -> return this.HandleDomainError(error)
        | Ok spaceId ->
            let! authResult = this.CheckAuthorization spaceId DashboardEdit

            match authResult with
            | Error _ -> return this.HandleAuthorizationError()
            | Ok() ->
                let! result = commandHandler.HandleCommand(UpdateDashboardName(this.CurrentUserId, id, updateDto))

                return
                    match result with
                    | Ok(DashboardResult dashboardDto) -> this.Ok(dashboardDto) :> IActionResult
                    | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                    | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/configuration")>]
    [<ProducesResponseType(typeof<DashboardData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateDashboardConfiguration
        (id: string, [<FromBody>] updateDto: UpdateDashboardConfigurationDto)
        : Task<IActionResult> =
        task {
            let! spaceIdResult = this.GetSpaceIdFromDashboard id

            match spaceIdResult with
            | Error error -> return this.HandleDomainError(error)
            | Ok spaceId ->
                let! authResult = this.CheckAuthorization spaceId DashboardEdit

                match authResult with
                | Error _ -> return this.HandleAuthorizationError()
                | Ok() ->
                    let! result =
                        commandHandler.HandleCommand(UpdateDashboardConfiguration(this.CurrentUserId, id, updateDto))

                    return
                        match result with
                        | Ok(DashboardResult dashboardDto) -> this.Ok(dashboardDto) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/prepare-app")>]
    [<ProducesResponseType(typeof<DashboardData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateDashboardPrepareApp
        (id: string, [<FromBody>] updateDto: UpdateDashboardPrepareAppDto)
        : Task<IActionResult> =
        task {
            let! spaceIdResult = this.GetSpaceIdFromDashboard id

            match spaceIdResult with
            | Error error -> return this.HandleDomainError(error)
            | Ok spaceId ->
                let! authResult = this.CheckAuthorization spaceId DashboardEdit

                match authResult with
                | Error _ -> return this.HandleAuthorizationError()
                | Ok() ->
                    let! result =
                        commandHandler.HandleCommand(UpdateDashboardPrepareApp(this.CurrentUserId, id, updateDto))

                    return
                        match result with
                        | Ok(DashboardResult dashboardDto) -> this.Ok(dashboardDto) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
        }

    [<HttpPost("{id}/prepare")>]
    [<ProducesResponseType(typeof<DashboardPrepareResponseDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.PrepareDashboard(id: string, [<FromBody>] prepareDto: PrepareDashboardDto) : Task<IActionResult> = task {
        let! spaceIdResult = this.GetSpaceIdFromDashboard id

        match spaceIdResult with
        | Error error -> return this.HandleDomainError(error)
        | Ok spaceId ->
            let! authResult = this.CheckRuntimeAuthorization spaceId

            match authResult with
            | Error _ -> return this.HandleAuthorizationError()
            | Ok() ->
                let! currentUserResult = this.GetCurrentUser()

                match currentUserResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok currentUser ->
                    let! result =
                        commandHandler.HandleCommand(PrepareDashboard(this.CurrentUserId, id, currentUser, prepareDto))

                    return
                        match result with
                        | Ok(DashboardPrepareResult response) -> this.Ok(response) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpPost("{id}/action/{actionId}")>]
    [<ProducesResponseType(typeof<DashboardActionResponseDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RunDashboardAction
        (id: string, actionId: string, [<FromBody>] actionDto: RunDashboardActionDto)
        : Task<IActionResult> =
        task {
            match ActionId.Create(Some actionId) with
            | Error error -> return this.HandleDomainError(error)
            | Ok actionIdObj ->
                let! spaceIdResult = this.GetSpaceIdFromDashboard id

                match spaceIdResult with
                | Error error -> return this.HandleDomainError(error)
                | Ok spaceId ->
                    let! authResult = this.CheckRuntimeAuthorization spaceId

                    match authResult with
                    | Error _ -> return this.HandleAuthorizationError()
                    | Ok() ->
                        let! currentUserResult = this.GetCurrentUser()

                        match currentUserResult with
                        | Error error -> return this.HandleDomainError(error)
                        | Ok currentUser ->
                            let! result =
                                commandHandler.HandleCommand(
                                    RunDashboardAction(this.CurrentUserId, id, actionIdObj, currentUser, actionDto)
                                )

                            return
                                match result with
                                | Ok(DashboardActionResult response) -> this.Ok(response) :> IActionResult
                                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                                | Error error -> this.HandleDomainError(error)
        }

    [<HttpDelete("{id}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.DeleteDashboard(id: string) : Task<IActionResult> = task {
        let! spaceIdResult = this.GetSpaceIdFromDashboard id

        match spaceIdResult with
        | Error error -> return this.HandleDomainError(error)
        | Ok spaceId ->
            let! authResult = this.CheckAuthorization spaceId DashboardDelete

            match authResult with
            | Error _ -> return this.HandleAuthorizationError()
            | Ok() ->
                let! result = commandHandler.HandleCommand(DeleteDashboard(this.CurrentUserId, id))

                return
                    match result with
                    | Ok(DashboardUnitResult()) -> this.NoContent() :> IActionResult
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