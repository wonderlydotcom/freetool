namespace Freetool.Api.Controllers

open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces

[<ApiController>]
[<Route("user")>]
type UserController
    (
        spaceRepository: ISpaceRepository,
        commandHandler: ICommandHandler<UserCommand, UserCommandResult>,
        authService: IAuthorizationService
    ) =
    inherit AuthenticatedControllerBase()

    // Helper to check if current user is an organization admin
    member private this.IsOrganizationAdmin() : Task<bool> = task {
        let userId = this.CurrentUserId

        return!
            authService.CheckPermissionAsync
                (User(userId.Value.ToString()))
                OrganizationAdmin
                (OrganizationObject "default")
    }

    // Helper to check if current user can modify a target user
    member private this.CanModifyUser(targetUserId: string) : Task<bool> = task {
        let currentUserId = this.CurrentUserId

        // Users can always modify themselves
        if currentUserId.Value.ToString() = targetUserId then
            return true
        else
            // Otherwise, check if they're an org admin
            return! this.IsOrganizationAdmin()
    }

    [<HttpGet("{id}")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetUserById(id: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetUserById id)

        return
            match result with
            | Ok(UserResult userDto) -> this.Ok(userDto) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("email/{email}")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetUserByEmail(email: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetUserByEmail email)

        return
            match result with
            | Ok(UserResult userDto) -> this.Ok(userDto) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet>]
    [<ProducesResponseType(typeof<PagedResult<UserWithRoleDto>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetUsers([<FromQuery>] skip: int, [<FromQuery>] take: int) : Task<IActionResult> = task {
        let skipValue = if skip < 0 then 0 else skip

        let takeValue =
            if take <= 0 then 50
            elif take > 100 then 100
            else take

        let! result = commandHandler.HandleCommand(GetAllUsers(skipValue, takeValue))

        match result with
        | Ok(UsersResult pagedUsers) ->
            // Enrich each user with org admin status
            let! enrichedUsers =
                pagedUsers.Items
                |> List.map (fun userData -> task {
                    let userIdStr = userData.Id.Value.ToString()

                    let! isOrgAdmin =
                        authService.CheckPermissionAsync
                            (User userIdStr)
                            OrganizationAdmin
                            (OrganizationObject "default")

                    let enrichedUser: UserWithRoleDto = {
                        Id = userIdStr
                        Name = userData.Name
                        Email = userData.Email
                        ProfilePicUrl = userData.ProfilePicUrl
                        InvitedAt = userData.InvitedAt
                        IsOrgAdmin = isOrgAdmin
                    }

                    return enrichedUser
                })
                |> Task.WhenAll

            let enrichedResult: PagedResult<UserWithRoleDto> = {
                Items = enrichedUsers |> Array.toList
                TotalCount = pagedUsers.TotalCount
                Skip = pagedUsers.Skip
                Take = pagedUsers.Take
            }

            return this.Ok(enrichedResult) :> IActionResult
        | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
        | Error error -> return this.HandleDomainError(error)
    }

    [<HttpGet("me")>]
    [<ProducesResponseType(typeof<CurrentUserDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetCurrentUser() : Task<IActionResult> = task {
        let userId = this.CurrentUserId
        let userIdStr = userId.Value.ToString()

        // Get user details
        let! result = commandHandler.HandleCommand(GetUserById userIdStr)

        match result with
        | Error error -> return this.HandleDomainError(error)
        | Ok(UserResult userData) ->
            // Check if user is org admin
            let! isOrgAdmin =
                authService.CheckPermissionAsync (User userIdStr) OrganizationAdmin (OrganizationObject "default")

            // Build teams from effective OpenFGA permissions so this stays correct
            // for identity-provisioned memberships.
            let! totalSpaceCount = spaceRepository.GetCountAsync()
            let! spaces = spaceRepository.GetAllAsync 0 totalSpaceCount

            let! teams = task {
                let mutable teamList = []

                for space in spaces do
                    let spaceData = space.State
                    let spaceIdStr = spaceData.Id.Value.ToString()

                    let! permissionMap =
                        authService.BatchCheckPermissionsAsync
                            (User userIdStr)
                            [ SpaceModerator; SpaceMember ]
                            (SpaceObject spaceIdStr)

                    let isModerator =
                        permissionMap |> Map.tryFind SpaceModerator |> Option.defaultValue false

                    let isMember = permissionMap |> Map.tryFind SpaceMember |> Option.defaultValue false

                    if isModerator || isMember then
                        let role = if isModerator then "moderator" else "member"

                        let teamDto: TeamMembershipDto = {
                            Id = spaceIdStr
                            Name = spaceData.Name
                            Role = role
                        }

                        teamList <- teamDto :: teamList

                return List.rev teamList
            }

            let currentUserDto: CurrentUserDto = {
                Id = userIdStr
                Name = userData.Name
                Email = userData.Email
                ProfilePicUrl = userData.ProfilePicUrl
                IsOrgAdmin = isOrgAdmin
                Teams = teams
            }

            return this.Ok(currentUserDto) :> IActionResult
        | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
    }

    [<HttpPost("invite")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.InviteUser([<FromBody>] inviteDto: InviteUserDto) : Task<IActionResult> = task {
        // Only organization admins can invite users
        let! isAdmin = this.IsOrganizationAdmin()

        if not isAdmin then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "Only organization admins can invite users"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId
            let! result = commandHandler.HandleCommand(InviteUser(userId, inviteDto))

            return
                match result with
                | Ok(UserResult userData) -> this.StatusCode(201, userData) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/name")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateUserName(id: string, [<FromBody>] updateDto: UpdateUserNameDto) : Task<IActionResult> = task {
        // Check authorization
        let! canModify = this.CanModifyUser(id)

        if not canModify then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to modify this user"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId
            let! result = commandHandler.HandleCommand(UpdateUserName(userId, id, updateDto))

            return
                match result with
                | Ok(UserResult userDto) -> this.Ok(userDto) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/email")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateUserEmail(id: string, [<FromBody>] updateDto: UpdateUserEmailDto) : Task<IActionResult> = task {
        // Check authorization
        let! canModify = this.CanModifyUser(id)

        if not canModify then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to modify this user"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId
            let! result = commandHandler.HandleCommand(UpdateUserEmail(userId, id, updateDto))

            return
                match result with
                | Ok(UserResult userDto) -> this.Ok userDto :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError error
    }

    [<HttpPut("{id}/profile-picture")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.SetProfilePicture(id: string, [<FromBody>] setDto: SetProfilePictureDto) : Task<IActionResult> = task {
        // Check authorization
        let! canModify = this.CanModifyUser(id)

        if not canModify then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to modify this user"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId

            let! result = commandHandler.HandleCommand(SetProfilePicture(userId, id, setDto))

            return
                match result with
                | Ok(UserResult userDto) -> this.Ok userDto :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError error
    }

    [<HttpDelete("{id}/profile-picture")>]
    [<ProducesResponseType(typeof<UserData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RemoveProfilePicture(id: string) : Task<IActionResult> = task {
        // Check authorization
        let! canModify = this.CanModifyUser(id)

        if not canModify then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to modify this user"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId

            let! result = commandHandler.HandleCommand(RemoveProfilePicture(userId, id))

            return
                match result with
                | Ok(UserResult userDto) -> this.Ok userDto :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError error
    }

    [<HttpDelete("{id}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.DeleteUser(id: string) : Task<IActionResult> = task {
        // Only organization admins can delete users
        let! isAdmin = this.IsOrganizationAdmin()

        if not isAdmin then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "Only organization admins can delete users"
                    |}
                )
                :> IActionResult
        else
            let userId = this.CurrentUserId

            let! result = commandHandler.HandleCommand(DeleteUser(userId, id))

            return
                match result with
                | Ok(UnitResult _) -> this.NoContent() :> IActionResult
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