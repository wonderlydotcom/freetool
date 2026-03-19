namespace Freetool.Api.Controllers

open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Application.DTOs
open Freetool.Application.Commands
open Freetool.Application.Interfaces

[<ApiController>]
[<Route("space")>]
type SpaceController
    (
        commandHandler: ICommandHandler<SpaceCommand, SpaceCommandResult>,
        authService: IAuthorizationService,
        logger: ILogger<SpaceController>
    ) =
    inherit AuthenticatedControllerBase()

    /// Checks if the current user is an organization administrator
    member private this.CheckIsOrgAdminAsync() : Task<bool> = task {
        let userId = this.CurrentUserId
        let userIdStr = userId.Value.ToString()

        return! authService.CheckPermissionAsync (User userIdStr) OrganizationAdmin (OrganizationObject "default")
    }

    /// Checks if the current user is the moderator of a specific space
    member private this.CheckIsSpaceModeratorAsync(spaceId: string) : Task<bool> = task {
        let userId = this.CurrentUserId
        let userIdStr = userId.Value.ToString()

        let! isModeratorByTuple = authService.CheckPermissionAsync (User userIdStr) SpaceModerator (SpaceObject spaceId)

        if isModeratorByTuple then
            return true
        else
            // Fallback: tolerate tuple drift by checking the persisted space moderator.
            let! spaceResult = commandHandler.HandleCommand(GetSpaceById spaceId)

            match spaceResult with
            | Ok(SpaceResult space) -> return space.ModeratorUserId = userId
            | _ -> return false
    }

    /// Checks if the current user is either an org admin OR the moderator of the specified space
    member private this.CheckIsOrgAdminOrModeratorAsync(spaceId: string) : Task<bool> = task {
        let! isOrgAdmin = this.CheckIsOrgAdminAsync()

        if isOrgAdmin then
            return true
        else
            return! this.CheckIsSpaceModeratorAsync(spaceId)
    }

    /// Returns a 403 Forbidden response with the given message
    member private this.Forbidden(message: string) : IActionResult =
        this.StatusCode(
            403,
            {|
                error = "Forbidden"
                message = message
            |}
        )
        :> IActionResult

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

    [<HttpPost>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.CreateSpace([<FromBody>] createDto: CreateSpaceDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Only org admins can create spaces
        let! isOrgAdmin = this.CheckIsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning("User {UserId} attempted to create space without org admin permissions", userId.Value)
            return this.Forbidden("Only organization administrators can create spaces")
        else
            // CreateSpace command takes CreateSpaceDto directly - validation happens in handler
            let! result = commandHandler.HandleCommand(CreateSpace(userId, createDto))

            match result with
            | Ok(SpaceResult spaceData) ->
                let spaceIdStr = spaceData.Id.Value.ToString()

                // Set up organization relation for the space so org admins inherit permissions
                do!
                    authService.CreateRelationshipsAsync(
                        [
                            {
                                Subject = Organization "default"
                                Relation = SpaceOrganization
                                Object = SpaceObject spaceIdStr
                            }
                        ]
                    )

                // Set the moderator relation
                let moderatorIdStr = spaceData.ModeratorUserId.Value.ToString()

                do!
                    authService.CreateRelationshipsAsync(
                        [
                            {
                                Subject = User moderatorIdStr
                                Relation = SpaceModerator
                                Object = SpaceObject spaceIdStr
                            }
                        ]
                    )

                // Set member relations for all members
                for memberId in spaceData.MemberIds do
                    let memberIdStr = memberId.Value.ToString()

                    do!
                        authService.CreateRelationshipsAsync(
                            [
                                {
                                    Subject = User memberIdStr
                                    Relation = SpaceMember
                                    Object = SpaceObject spaceIdStr
                                }
                            ]
                        )

                logger.LogInformation("Space {SpaceId} created by user {UserId}", spaceIdStr, userId.Value)

                return this.CreatedAtAction(nameof this.GetSpaceById, {| id = spaceIdStr |}, spaceData) :> IActionResult
            | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> return this.HandleDomainError(error)
    }

    [<HttpGet("{id}")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSpaceById(id: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetSpaceById id)

        return
            match result with
            | Ok(SpaceResult spaceDto) -> this.Ok(spaceDto) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("name/{name}")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSpaceByName(name: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetSpaceByName name)

        return
            match result with
            | Ok(SpaceResult spaceDto) -> this.Ok(spaceDto) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet>]
    [<ProducesResponseType(typeof<PagedResult<SpaceData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSpaces([<FromQuery>] skip: int, [<FromQuery>] take: int) : Task<IActionResult> = task {
        let skipValue = if skip < 0 then 0 else skip

        let takeValue =
            if take <= 0 then 50
            elif take > 100 then 100
            else take

        let! isOrgAdmin = this.CheckIsOrgAdminAsync()

        let! result =
            if isOrgAdmin then
                // Org admins can see all spaces
                commandHandler.HandleCommand(GetAllSpaces(skipValue, takeValue))
            else
                // Regular users see only spaces they're members/moderators of
                let userId = this.CurrentUserId
                commandHandler.HandleCommand(GetSpacesByUserId(userId.Value.ToString(), skipValue, takeValue))

        return
            match result with
            | Ok(SpacesResult pagedSpaces) -> this.Ok(pagedSpaces) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("user/{userId}")>]
    [<ProducesResponseType(typeof<PagedResult<SpaceData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSpacesByUserId
        (userId: string, [<FromQuery>] skip: int, [<FromQuery>] take: int)
        : Task<IActionResult> =
        task {
            let skipValue = if skip < 0 then 0 else skip

            let takeValue =
                if take <= 0 then 50
                elif take > 100 then 100
                else take

            let! isOrgAdmin = this.CheckIsOrgAdminAsync()

            let! result =
                if isOrgAdmin then
                    // Org admins can see all spaces
                    commandHandler.HandleCommand(GetAllSpaces(skipValue, takeValue))
                else
                    // Regular users see only spaces they're members/moderators of
                    commandHandler.HandleCommand(GetSpacesByUserId(userId, skipValue, takeValue))

            return
                match result with
                | Ok(SpacesResult pagedSpaces) -> this.Ok(pagedSpaces) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/name")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateSpaceName(id: string, [<FromBody>] updateDto: UpdateSpaceNameDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Org admins or space moderators can rename spaces
        let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

        if not hasPermission then
            logger.LogWarning("User {UserId} attempted to rename space {SpaceId} without permission", userId.Value, id)

            return this.Forbidden("Only organization administrators or space moderators can rename spaces")
        else
            let! result = commandHandler.HandleCommand(UpdateSpaceName(userId, id, updateDto))

            return
                match result with
                | Ok(SpaceResult spaceDto) -> this.Ok(spaceDto) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/moderator")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.ChangeModerator(id: string, [<FromBody>] changeModeratorDto: ChangeModeratorDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Only org admins can change moderators
        let! isOrgAdmin = this.CheckIsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning(
                "User {UserId} attempted to change moderator of space {SpaceId} without org admin permissions",
                userId.Value,
                id
            )

            return this.Forbidden("Only organization administrators can change space moderators")
        else
            // Get the current space to find the old moderator
            let! getResult = commandHandler.HandleCommand(GetSpaceById id)

            match getResult with
            | Error error -> return this.HandleDomainError(error)
            | Ok(SpaceResult currentSpace) ->
                let! result = commandHandler.HandleCommand(ChangeModerator(userId, id, changeModeratorDto))

                match result with
                | Ok(SpaceResult spaceData) ->
                    let spaceIdStr = spaceData.Id.Value.ToString()
                    let oldModeratorIdStr = currentSpace.ModeratorUserId.Value.ToString()
                    let newModeratorIdStr = spaceData.ModeratorUserId.Value.ToString()

                    // Update OpenFGA relations: remove old moderator, add new moderator
                    if oldModeratorIdStr <> newModeratorIdStr then
                        do!
                            authService.UpdateRelationshipsAsync(
                                {
                                    TuplesToAdd = [
                                        {
                                            Subject = User newModeratorIdStr
                                            Relation = SpaceModerator
                                            Object = SpaceObject spaceIdStr
                                        }
                                    ]
                                    TuplesToRemove = [
                                        {
                                            Subject = User oldModeratorIdStr
                                            Relation = SpaceModerator
                                            Object = SpaceObject spaceIdStr
                                        }
                                    ]
                                }
                            )

                        logger.LogInformation(
                            "Moderator changed for space {SpaceId} from {OldModeratorId} to {NewModeratorId} by user {UserId}",
                            spaceIdStr,
                            oldModeratorIdStr,
                            newModeratorIdStr,
                            userId.Value
                        )

                    return this.Ok(spaceData) :> IActionResult
                | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> return this.HandleDomainError(error)
            | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
    }

    [<HttpPost("{id}/members")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.AddMember(id: string, [<FromBody>] addMemberDto: AddMemberDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Org admins or space moderators can add members
        let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

        if not hasPermission then
            logger.LogWarning(
                "User {UserId} attempted to add member to space {SpaceId} without permission",
                userId.Value,
                id
            )

            return this.Forbidden("Only organization administrators or space moderators can add members to spaces")
        else
            let! result = commandHandler.HandleCommand(AddMember(userId, id, addMemberDto))

            match result with
            | Ok(SpaceResult spaceData) ->
                let spaceIdStr = spaceData.Id.Value.ToString()
                let newMemberIdStr = System.Guid.Parse(addMemberDto.UserId).ToString()

                // Add member relation in OpenFGA
                do!
                    authService.CreateRelationshipsAsync(
                        [
                            {
                                Subject = User newMemberIdStr
                                Relation = SpaceMember
                                Object = SpaceObject spaceIdStr
                            }
                        ]
                    )

                logger.LogInformation(
                    "Member {MemberId} added to space {SpaceId} by user {UserId}",
                    newMemberIdStr,
                    spaceIdStr,
                    userId.Value
                )

                return this.Ok(spaceData) :> IActionResult
            | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> return this.HandleDomainError(error)
    }

    [<HttpDelete("{id}/members/{memberId}")>]
    [<ProducesResponseType(typeof<SpaceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.RemoveMember(id: string, memberId: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Org admins or space moderators can remove members
        let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

        if not hasPermission then
            logger.LogWarning(
                "User {UserId} attempted to remove member from space {SpaceId} without permission",
                userId.Value,
                id
            )

            return this.Forbidden("Only organization administrators or space moderators can remove members from spaces")
        else
            let removeDto: RemoveMemberDto = { UserId = memberId }
            let! result = commandHandler.HandleCommand(RemoveMember(userId, id, removeDto))

            match result with
            | Ok(SpaceResult spaceData) ->
                let spaceIdStr = spaceData.Id.Value.ToString()

                // Remove member relation in OpenFGA
                do!
                    authService.DeleteRelationshipsAsync(
                        [
                            {
                                Subject = User memberId
                                Relation = SpaceMember
                                Object = SpaceObject spaceIdStr
                            }
                        ]
                    )

                logger.LogInformation(
                    "Member {MemberId} removed from space {SpaceId} by user {UserId}",
                    memberId,
                    spaceIdStr,
                    userId.Value
                )

                return this.Ok(spaceData) :> IActionResult
            | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> return this.HandleDomainError(error)
    }

    [<HttpDelete("{id}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.DeleteSpace(id: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Only org admins can delete spaces
        let! isOrgAdmin = this.CheckIsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning(
                "User {UserId} attempted to delete space {SpaceId} without org admin permissions",
                userId.Value,
                id
            )

            return this.Forbidden("Only organization administrators can delete spaces")
        else
            // Get the current space to clean up OpenFGA relations
            let! getResult = commandHandler.HandleCommand(GetSpaceById id)

            match getResult with
            | Error error -> return this.HandleDomainError(error)
            | Ok(SpaceResult currentSpace) ->
                let! result = commandHandler.HandleCommand(DeleteSpace(userId, id))

                match result with
                | Ok(SpaceCommandResult.UnitResult()) ->
                    let spaceIdStr = currentSpace.Id.Value.ToString()

                    // Clean up all OpenFGA relations for this space
                    let moderatorIdStr = currentSpace.ModeratorUserId.Value.ToString()

                    // Remove organization relation
                    do!
                        authService.DeleteRelationshipsAsync(
                            [
                                {
                                    Subject = Organization "default"
                                    Relation = SpaceOrganization
                                    Object = SpaceObject spaceIdStr
                                }
                            ]
                        )

                    // Remove moderator relation
                    do!
                        authService.DeleteRelationshipsAsync(
                            [
                                {
                                    Subject = User moderatorIdStr
                                    Relation = SpaceModerator
                                    Object = SpaceObject spaceIdStr
                                }
                            ]
                        )

                    // Remove all member relations
                    for memberId in currentSpace.MemberIds do
                        let memberIdStr = memberId.Value.ToString()

                        do!
                            authService.DeleteRelationshipsAsync(
                                [
                                    {
                                        Subject = User memberIdStr
                                        Relation = SpaceMember
                                        Object = SpaceObject spaceIdStr
                                    }
                                ]
                            )

                    logger.LogInformation("Space {SpaceId} deleted by user {UserId}", spaceIdStr, userId.Value)

                    return this.NoContent() :> IActionResult
                | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> return this.HandleDomainError(error)
            | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
    }

    /// Checks if the current user is a member (including moderator) of a specific space
    member private this.CheckIsSpaceMemberAsync(spaceId: string) : Task<bool> = task {
        let userId = this.CurrentUserId
        let userIdStr = userId.Value.ToString()

        // Check if user is moderator
        let! isModerator = authService.CheckPermissionAsync (User userIdStr) SpaceModerator (SpaceObject spaceId)

        if isModerator then
            return true
        else
            // Check if user is member
            let! isMemberByTuple = authService.CheckPermissionAsync (User userIdStr) SpaceMember (SpaceObject spaceId)

            if isMemberByTuple then
                return true
            else
                // Fallback: tolerate tuple drift by checking persisted membership data.
                let! spaceResult = commandHandler.HandleCommand(GetSpaceById spaceId)

                match spaceResult with
                | Ok(SpaceResult space) ->
                    return space.ModeratorUserId = userId || (space.MemberIds |> List.contains userId)
                | _ -> return false
    }

    [<HttpGet("{id}/permissions")>]
    [<ProducesResponseType(typeof<SpaceMembersPermissionsResponseDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSpacePermissions
        (id: string, [<FromQuery>] skip: int, [<FromQuery>] take: int)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            // Any space member (moderator or member) or org admin can view permissions
            let! isOrgAdmin = this.CheckIsOrgAdminAsync()
            let! isMember = this.CheckIsSpaceMemberAsync(id)

            if not isOrgAdmin && not isMember then
                logger.LogWarning(
                    "User {UserId} attempted to view permissions for space {SpaceId} without being a member",
                    userId.Value,
                    id
                )

                return this.Forbidden("Only space members can view space permissions")
            else
                let skipValue = if skip < 0 then 0 else skip

                let takeValue =
                    if take <= 0 then 50
                    elif take > 100 then 100
                    else take

                let! result = commandHandler.HandleCommand(GetSpaceMembersWithPermissions(id, skipValue, takeValue))

                return
                    match result with
                    | Ok(SpaceMembersPermissionsResult response) -> this.Ok(response) :> IActionResult
                    | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                    | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/permissions")>]
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateUserPermissions
        (id: string, [<FromBody>] updateDto: UpdateUserPermissionsDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            // Only moderators and org admins can update permissions
            let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

            if not hasPermission then
                logger.LogWarning(
                    "User {UserId} attempted to update permissions for space {SpaceId} without moderator/admin permissions",
                    userId.Value,
                    id
                )

                return
                    this.Forbidden("Only organization administrators or space moderators can update member permissions")
            else
                let! result = commandHandler.HandleCommand(UpdateUserPermissions(userId, id, updateDto))

                match result with
                | Ok(SpaceCommandResult.UnitResult()) ->
                    logger.LogInformation(
                        "Permissions updated for user {TargetUserId} in space {SpaceId} by user {ActorUserId}",
                        updateDto.UserId,
                        id,
                        userId.Value
                    )

                    return this.Ok() :> IActionResult
                | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> return this.HandleDomainError(error)
        }

    [<HttpGet("{id}/default-member-permissions")>]
    [<ProducesResponseType(typeof<SpaceDefaultMemberPermissionsResponseDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetDefaultMemberPermissions(id: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId
        let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

        if not hasPermission then
            logger.LogWarning(
                "User {UserId} attempted to view default member permissions for space {SpaceId} without moderator/admin permissions",
                userId.Value,
                id
            )

            return
                this.Forbidden(
                    "Only organization administrators or space moderators can view default member permissions"
                )
        else
            let! result = commandHandler.HandleCommand(GetDefaultMemberPermissions id)

            return
                match result with
                | Ok(SpaceDefaultMemberPermissionsResult response) -> this.Ok(response) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/default-member-permissions")>]
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateDefaultMemberPermissions
        (id: string, [<FromBody>] updateDto: UpdateDefaultMemberPermissionsDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId
            let! hasPermission = this.CheckIsOrgAdminOrModeratorAsync(id)

            if not hasPermission then
                logger.LogWarning(
                    "User {UserId} attempted to update default member permissions for space {SpaceId} without moderator/admin permissions",
                    userId.Value,
                    id
                )

                return
                    this.Forbidden(
                        "Only organization administrators or space moderators can update default member permissions"
                    )
            else
                let! result = commandHandler.HandleCommand(UpdateDefaultMemberPermissions(userId, id, updateDto))

                match result with
                | Ok(SpaceCommandResult.UnitResult()) ->
                    logger.LogInformation(
                        "Default member permissions updated for space {SpaceId} by user {ActorUserId}",
                        id,
                        userId.Value
                    )

                    return this.Ok() :> IActionResult
                | Ok _ -> return this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> return this.HandleDomainError(error)
        }