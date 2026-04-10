namespace Freetool.Api.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Freetool.Api.Services
open Freetool.Application.Interfaces

[<Route("admin/openfga")>]
type OpenFgaRepairController
    (
        repairService: IOpenFgaSpaceAuthorizationRepairService,
        authService: IAuthorizationService,
        logger: ILogger<OpenFgaRepairController>
    ) =
    inherit AuthenticatedControllerBase()

    member private this.IsOrgAdminAsync() : Task<bool> = task {
        let userId = this.CurrentUserId.Value.ToString()

        return!
            authService.CheckPermissionAsync
                (Freetool.Application.Interfaces.User userId)
                OrganizationAdmin
                (OrganizationObject "default")
    }

    [<HttpPost("repair-default-member-permissions")>]
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    [<ProducesResponseType(StatusCodes.Status403Forbidden)>]
    member this.RepairDefaultMemberPermissions
        ([<FromQuery>] apply: bool, [<FromQuery>] spaceId: string)
        : Task<IActionResult> =
        task {
            let actorUserId = this.CurrentUserId
            let! isOrgAdmin = this.IsOrgAdminAsync()

            if not isOrgAdmin then
                logger.LogWarning(
                    "User {UserId} attempted to repair OpenFGA space authorization tuples without org admin role",
                    actorUserId.Value
                )

                return
                    this.StatusCode(
                        403,
                        {|
                            error = "Forbidden"
                            message = "Only organization administrators can repair OpenFGA space authorization tuples"
                        |}
                    )
                    :> IActionResult
            else
                let requestedSpaceId =
                    spaceId
                    |> Option.ofObj
                    |> Option.map (fun value -> value.Trim())
                    |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))

                match requestedSpaceId with
                | Some value when not (Guid.TryParse value |> fst) ->
                    return
                        this.BadRequest(
                            {|
                                error = "Validation failed"
                                message = "Invalid space ID format"
                            |}
                        )
                        :> IActionResult
                | _ ->
                    let! summary = repairService.RepairAsync apply requestedSpaceId
                    return this.Ok(summary) :> IActionResult
        }