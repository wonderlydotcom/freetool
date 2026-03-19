namespace Freetool.Api.Controllers

open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects

/// Controller for OpenFGA initialization operations
[<ApiController>]
[<Route("admin/openfga")>]
type OpenFgaInitController
    (authService: IAuthorizationService, userRepository: IUserRepository, configuration: IConfiguration) =
    inherit ControllerBase()

    /// Writes the authorization model to the configured OpenFGA store
    /// Call this endpoint once after setting up your StoreId in configuration
    [<HttpPost("write-model")>]
    [<ProducesResponseType(typeof<
                               {|
                                   authorizationModelId: string
                                   message: string
                                   orgAdminConfigured: bool
                               |}
                            >,
                           StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status500InternalServerError)>]
    member this.WriteAuthorizationModel() : Task<IActionResult> = task {
        try
            // Write the authorization model
            let! modelResponse = authService.WriteAuthorizationModelAsync()

            // Initialize organization admin if configured
            let orgAdminEmail = configuration["OpenFGA:OrgAdminEmail"]
            let mutable orgAdminConfigured = false

            if not (System.String.IsNullOrEmpty(orgAdminEmail)) then
                // Convert string to Email value object
                match Email.Create(Some orgAdminEmail) with
                | Ok email ->
                    let! userOption = userRepository.GetByEmailAsync email

                    match userOption with
                    | Some user ->
                        let userId = user.State.Id.ToString()
                        do! authService.InitializeOrganizationAsync "default" userId
                        orgAdminConfigured <- true
                    | None -> ()
                | Error _ -> ()

            let message =
                if orgAdminConfigured then
                    $"Authorization model written successfully and organization admin set to {orgAdminEmail}. OpenFGA is now ready to use."
                else
                    "Authorization model written successfully. OpenFGA is now ready to use."

            return
                this.Ok(
                    {|
                        authorizationModelId = modelResponse.AuthorizationModelId
                        message = message
                        orgAdminConfigured = orgAdminConfigured
                    |}
                )
                :> IActionResult
        with ex ->
            return
                this.StatusCode(
                    500,
                    {|
                        error = "Failed to write authorization model"
                        message = ex.Message
                        details =
                            "Make sure you have configured the StoreId in your application settings and that OpenFGA is running."
                    |}
                )
                :> IActionResult
    }