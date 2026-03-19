namespace Freetool.Api.Controllers

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
[<Route("resource")>]
type ResourceController
    (
        commandHandler: ICommandHandler<ResourceCommand, ResourceCommandResult>,
        authorizationService: IAuthorizationService,
        resourceRepository: IResourceRepository,
        sqlMetadataService: ISqlMetadataService
    ) =
    inherit AuthenticatedControllerBase()

    [<HttpPost("http")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.CreateHttpResource([<FromBody>] createDto: CreateHttpResourceDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Check authorization: user must have create_resource permission on the space
        let! canCreate =
            authorizationService.CheckPermissionAsync
                (User(userId.ToString()))
                ResourceCreate
                (SpaceObject createDto.SpaceId)

        if not canCreate then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to create resources in this space"
                    |}
                )
                :> IActionResult
        else
            match ResourceMapper.fromCreateHttpDto userId createDto with
            | Error domainError -> return this.HandleDomainError(domainError)
            | Ok validatedResource ->
                let! result = commandHandler.HandleCommand(CreateResource(userId, validatedResource))

                return
                    match result with
                    | Ok(ResourceResult resourceDto) ->
                        let sanitized = ResponseSanitizer.sanitizeResource resourceDto

                        this.CreatedAtAction(nameof this.GetResourceById, {| id = sanitized.Id |}, sanitized)
                        :> IActionResult
                    | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                    | Error error -> this.HandleDomainError(error)
    }

    [<HttpPost("sql")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status201Created)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.CreateSqlResource([<FromBody>] createDto: CreateSqlResourceDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Check authorization: user must have create_resource permission on the space
        let! canCreate =
            authorizationService.CheckPermissionAsync
                (User(userId.ToString()))
                ResourceCreate
                (SpaceObject createDto.SpaceId)

        if not canCreate then
            return
                this.StatusCode(
                    403,
                    {|
                        error = "Forbidden"
                        message = "You do not have permission to create resources in this space"
                    |}
                )
                :> IActionResult
        else
            match ResourceMapper.fromCreateSqlDto userId createDto with
            | Error domainError -> return this.HandleDomainError(domainError)
            | Ok validatedResource ->
                let! result = commandHandler.HandleCommand(CreateResource(userId, validatedResource))

                return
                    match result with
                    | Ok(ResourceResult resourceDto) ->
                        let sanitized = ResponseSanitizer.sanitizeResource resourceDto

                        this.CreatedAtAction(nameof this.GetResourceById, {| id = sanitized.Id |}, sanitized)
                        :> IActionResult
                    | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                    | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetResourceById(id: string) : Task<IActionResult> = task {
        let! result = commandHandler.HandleCommand(GetResourceById id)

        return
            match result with
            | Ok(ResourceResult resourceDto) ->
                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                this.Ok(sanitized) :> IActionResult
            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}/schema/tables")>]
    [<ProducesResponseType(typeof<SqlTableInfoDto list>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSqlTables(id: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        match System.Guid.TryParse id with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
        | true, guid ->
            let resourceId = ResourceId.FromGuid guid
            let! resourceOption = resourceRepository.GetByIdAsync resourceId

            match resourceOption with
            | None -> return this.HandleDomainError(NotFound "Resource not found")
            | Some resource ->
                let spaceId = Resource.getSpaceId resource

                let! canCreateApp =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        AppCreate
                        (SpaceObject(spaceId.ToString()))

                if not canCreateApp then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to access SQL schema for this space"
                            |}
                        )
                        :> IActionResult
                else
                    match Resource.getResourceKind resource with
                    | ResourceKind.Http ->
                        return this.HandleDomainError(ValidationError "Resource is not a SQL resource")
                    | ResourceKind.Sql ->
                        let! result = sqlMetadataService.GetTablesAsync resource.State

                        return
                            match result with
                            | Ok tables -> this.Ok(tables) :> IActionResult
                            | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("{id}/schema/columns")>]
    [<ProducesResponseType(typeof<SqlColumnInfoDto list>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetSqlColumns(id: string, [<FromQuery>] table: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        if System.String.IsNullOrWhiteSpace(table) then
            return this.HandleDomainError(ValidationError "Table name is required")
        else
            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canCreateApp =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            AppCreate
                            (SpaceObject(spaceId.ToString()))

                    if not canCreateApp then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to access SQL schema for this space"
                                |}
                            )
                            :> IActionResult
                    else
                        match Resource.getResourceKind resource with
                        | ResourceKind.Http ->
                            return this.HandleDomainError(ValidationError "Resource is not a SQL resource")
                        | ResourceKind.Sql ->
                            let! result = sqlMetadataService.GetColumnsAsync resource.State table

                            return
                                match result with
                                | Ok columns -> this.Ok(columns) :> IActionResult
                                | Error error -> this.HandleDomainError(error)
    }

    [<HttpGet("/space/{spaceId}/resource")>]
    [<ProducesResponseType(typeof<PagedResult<ResourceData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.GetResources(spaceId: string, [<FromQuery>] skip: int, [<FromQuery>] take: int) : Task<IActionResult> = task {
        match System.Guid.TryParse spaceId with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid space ID format")
        | true, guid ->
            let spaceIdObj = SpaceId.FromGuid guid
            let skipValue = if skip < 0 then 0 else skip

            let takeValue =
                if take <= 0 then 50
                elif take > 100 then 100
                else take

            let! result = commandHandler.HandleCommand(GetAllResources(spaceIdObj, skipValue, takeValue))

            return
                match result with
                | Ok(ResourcesResult pagedResources) ->
                    let sanitized = ResponseSanitizer.sanitizeResources pagedResources
                    this.Ok(sanitized) :> IActionResult
                | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/name")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceName(id: string, [<FromBody>] updateDto: UpdateResourceNameDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Check authorization: fetch resource to get space ID
        match System.Guid.TryParse id with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
        | true, guid ->
            let resourceId = ResourceId.FromGuid guid
            let! resourceOption = resourceRepository.GetByIdAsync resourceId

            match resourceOption with
            | None -> return this.HandleDomainError(NotFound "Resource not found")
            | Some resource ->
                let spaceId = Resource.getSpaceId resource

                let! canEdit =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        ResourceEdit
                        (SpaceObject(spaceId.ToString()))

                if not canEdit then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to edit resources in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(UpdateResourceName(userId, id, updateDto))

                    return
                        match result with
                        | Ok(ResourceResult resourceDto) ->
                            let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                            this.Ok(sanitized) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/description")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceDescription
        (id: string, [<FromBody>] updateDto: UpdateResourceDescriptionDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            // Check authorization
            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canEdit =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            ResourceEdit
                            (SpaceObject(spaceId.ToString()))

                    if not canEdit then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to edit resources in this space"
                                |}
                            )
                            :> IActionResult
                    else
                        let! result = commandHandler.HandleCommand(UpdateResourceDescription(userId, id, updateDto))

                        return
                            match result with
                            | Ok(ResourceResult resourceDto) ->
                                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                                this.Ok(sanitized) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/base-url")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceBaseUrl
        (id: string, [<FromBody>] updateDto: UpdateResourceBaseUrlDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canEdit =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            ResourceEdit
                            (SpaceObject(spaceId.ToString()))

                    if not canEdit then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to edit resources in this space"
                                |}
                            )
                            :> IActionResult
                    else
                        let! result = commandHandler.HandleCommand(UpdateResourceBaseUrl(userId, id, updateDto))

                        return
                            match result with
                            | Ok(ResourceResult resourceDto) ->
                                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                                this.Ok(sanitized) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/url-parameters")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceUrlParameters
        (id: string, [<FromBody>] updateDto: UpdateResourceUrlParametersDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canEdit =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            ResourceEdit
                            (SpaceObject(spaceId.ToString()))

                    if not canEdit then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to edit resources in this space"
                                |}
                            )
                            :> IActionResult
                    else
                        let! result = commandHandler.HandleCommand(UpdateResourceUrlParameters(userId, id, updateDto))

                        return
                            match result with
                            | Ok(ResourceResult resourceDto) ->
                                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                                this.Ok(sanitized) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/headers")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceHeaders
        (id: string, [<FromBody>] updateDto: UpdateResourceHeadersDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canEdit =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            ResourceEdit
                            (SpaceObject(spaceId.ToString()))

                    if not canEdit then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to edit resources in this space"
                                |}
                            )
                            :> IActionResult
                    else
                        let! result = commandHandler.HandleCommand(UpdateResourceHeaders(userId, id, updateDto))

                        return
                            match result with
                            | Ok(ResourceResult resourceDto) ->
                                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                                this.Ok(sanitized) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpPut("{id}/body")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceBody(id: string, [<FromBody>] updateDto: UpdateResourceBodyDto) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        match System.Guid.TryParse id with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
        | true, guid ->
            let resourceId = ResourceId.FromGuid guid
            let! resourceOption = resourceRepository.GetByIdAsync resourceId

            match resourceOption with
            | None -> return this.HandleDomainError(NotFound "Resource not found")
            | Some resource ->
                let spaceId = Resource.getSpaceId resource

                let! canEdit =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        ResourceEdit
                        (SpaceObject(spaceId.ToString()))

                if not canEdit then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to edit resources in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(UpdateResourceBody(userId, id, updateDto))

                    return
                        match result with
                        | Ok(ResourceResult resourceDto) ->
                            let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                            this.Ok(sanitized) :> IActionResult
                        | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                        | Error error -> this.HandleDomainError(error)
    }

    [<HttpPut("{id}/database-config")>]
    [<ProducesResponseType(typeof<ResourceData>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.UpdateResourceDatabaseConfig
        (id: string, [<FromBody>] updateDto: UpdateResourceDatabaseConfigDto)
        : Task<IActionResult> =
        task {
            let userId = this.CurrentUserId

            match System.Guid.TryParse id with
            | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
            | true, guid ->
                let resourceId = ResourceId.FromGuid guid
                let! resourceOption = resourceRepository.GetByIdAsync resourceId

                match resourceOption with
                | None -> return this.HandleDomainError(NotFound "Resource not found")
                | Some resource ->
                    let spaceId = Resource.getSpaceId resource

                    let! canEdit =
                        authorizationService.CheckPermissionAsync
                            (User(userId.ToString()))
                            ResourceEdit
                            (SpaceObject(spaceId.ToString()))

                    if not canEdit then
                        return
                            this.StatusCode(
                                403,
                                {|
                                    error = "Forbidden"
                                    message = "You do not have permission to edit resources in this space"
                                |}
                            )
                            :> IActionResult
                    else
                        let! result = commandHandler.HandleCommand(UpdateResourceDatabaseConfig(userId, id, updateDto))

                        return
                            match result with
                            | Ok(ResourceResult resourceDto) ->
                                let sanitized = ResponseSanitizer.sanitizeResource resourceDto
                                this.Ok(sanitized) :> IActionResult
                            | Ok _ -> this.StatusCode(500, "Unexpected result type") :> IActionResult
                            | Error error -> this.HandleDomainError(error)
        }

    [<HttpDelete("{id}")>]
    [<ProducesResponseType(StatusCodes.Status204NoContent)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.DeleteResource(id: string) : Task<IActionResult> = task {
        let userId = this.CurrentUserId

        // Check authorization: user must have delete_resource permission on the space
        match System.Guid.TryParse id with
        | false, _ -> return this.HandleDomainError(ValidationError "Invalid resource ID format")
        | true, guid ->
            let resourceId = ResourceId.FromGuid guid
            let! resourceOption = resourceRepository.GetByIdAsync resourceId

            match resourceOption with
            | None -> return this.HandleDomainError(NotFound "Resource not found")
            | Some resource ->
                let spaceId = Resource.getSpaceId resource

                let! canDelete =
                    authorizationService.CheckPermissionAsync
                        (User(userId.ToString()))
                        ResourceDelete
                        (SpaceObject(spaceId.ToString()))

                if not canDelete then
                    return
                        this.StatusCode(
                            403,
                            {|
                                error = "Forbidden"
                                message = "You do not have permission to delete resources in this space"
                            |}
                        )
                        :> IActionResult
                else
                    let! result = commandHandler.HandleCommand(DeleteResource(userId, id))

                    return
                        match result with
                        | Ok(ResourceUnitResult _) -> this.NoContent() :> IActionResult
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