namespace Freetool.Api.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Interfaces

[<ApiController>]
[<Route("org/identity/group-space-mappings")>]
type IdentityProvisioningController
    (
        mappingRepository: IIdentityGroupSpaceMappingRepository,
        authService: IAuthorizationService,
        logger: ILogger<IdentityProvisioningController>
    ) =
    inherit AuthenticatedControllerBase()

    member private this.IsOrgAdminAsync() : Task<bool> = task {
        let userId = this.CurrentUserId.Value.ToString()
        return! authService.CheckPermissionAsync (User userId) OrganizationAdmin (OrganizationObject "default")
    }

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

    [<HttpGet>]
    [<ProducesResponseType(typeof<IdentityGroupSpaceMappingDto list>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    member this.GetMappings() : Task<IActionResult> = task {
        let userId = this.CurrentUserId
        let! isOrgAdmin = this.IsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning(
                "User {UserId} attempted to list group-space mappings without org admin role",
                userId.Value
            )

            return this.Forbidden("Only organization administrators can view group-space mappings")
        else
            let! mappings = mappingRepository.GetAllAsync()
            return this.Ok(mappings) :> IActionResult
    }

    [<HttpPost>]
    [<ProducesResponseType(typeof<IdentityGroupSpaceMappingDto>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    member this.CreateMapping([<FromBody>] createDto: CreateIdentityGroupSpaceMappingDto) : Task<IActionResult> = task {
        let actorUserId = this.CurrentUserId
        let! isOrgAdmin = this.IsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning("User {UserId} attempted to create mapping without org admin role", actorUserId.Value)
            return this.Forbidden("Only organization administrators can create group-space mappings")
        else
            match Guid.TryParse createDto.SpaceId with
            | false, _ ->
                return
                    this.BadRequest(
                        {|
                            error = "Validation failed"
                            message = "Invalid space ID format"
                        |}
                    )
                    :> IActionResult
            | true, spaceGuid ->
                let spaceId = SpaceId.FromGuid spaceGuid
                let! result = mappingRepository.AddAsync actorUserId createDto.GroupKey spaceId

                match result with
                | Ok mapping -> return this.StatusCode(StatusCodes.Status201Created, mapping) :> IActionResult
                | Error error -> return this.HandleDomainError(error)
    }

    [<HttpPut("{mappingId}")>]
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    member this.UpdateMapping
        (mappingId: string, [<FromBody>] updateDto: UpdateIdentityGroupSpaceMappingDto)
        : Task<IActionResult> =
        task {
            let actorUserId = this.CurrentUserId
            let! isOrgAdmin = this.IsOrgAdminAsync()

            if not isOrgAdmin then
                logger.LogWarning("User {UserId} attempted to update mapping without org admin role", actorUserId.Value)
                return this.Forbidden("Only organization administrators can update group-space mappings")
            else
                match Guid.TryParse mappingId with
                | false, _ ->
                    return
                        this.BadRequest(
                            {|
                                error = "Validation failed"
                                message = "Invalid mapping ID format"
                            |}
                        )
                        :> IActionResult
                | true, mappingGuid ->
                    let! result = mappingRepository.UpdateIsActiveAsync actorUserId mappingGuid updateDto.IsActive

                    match result with
                    | Ok() -> return this.Ok() :> IActionResult
                    | Error error -> return this.HandleDomainError(error)
        }

    [<HttpDelete("{mappingId}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status409Conflict)>]
    member this.DeleteMapping(mappingId: string) : Task<IActionResult> = task {
        let actorUserId = this.CurrentUserId
        let! isOrgAdmin = this.IsOrgAdminAsync()

        if not isOrgAdmin then
            logger.LogWarning("User {UserId} attempted to delete mapping without org admin role", actorUserId.Value)
            return this.Forbidden("Only organization administrators can delete group-space mappings")
        else
            match Guid.TryParse mappingId with
            | false, _ ->
                return
                    this.BadRequest(
                        {|
                            error = "Validation failed"
                            message = "Invalid mapping ID format"
                        |}
                    )
                    :> IActionResult
            | true, mappingGuid ->
                let! result = mappingRepository.DeleteAsync mappingGuid

                match result with
                | Ok() -> return this.NoContent() :> IActionResult
                | Error error -> return this.HandleDomainError(error)
    }