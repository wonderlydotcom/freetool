namespace Freetool.Api.Controllers

open System.Threading.Tasks
open System.Text.Json
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Application.Interfaces

/// Response DTO for dev mode status
type DevModeResponseDto = { DevMode: bool }

/// Response DTO for a dev user
type DevUserDto = {
    Id: string
    Name: string
    Email: string
}

[<ApiController>]
[<Route("dev")>]
type DevController(userRepository: IUserRepository) =
    inherit ControllerBase()

    let isDevMode =
        System.Environment.GetEnvironmentVariable("FREETOOL_DEV_MODE") = "true"

    let jsonOptions =
        let opts = JsonSerializerOptions()
        opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        opts

    [<HttpGet("mode")>]
    [<ProducesResponseType(typeof<DevModeResponseDto>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    member this.GetDevMode() : IActionResult =
        if not isDevMode then
            this.NotFound() :> IActionResult
        else
            // Return as ContentResult to avoid chunked encoding issues
            let json = JsonSerializer.Serialize({ DevMode = true }, jsonOptions)
            this.Content(json, "application/json") :> IActionResult

    [<HttpGet("users")>]
    [<ProducesResponseType(typeof<DevUserDto list>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status404NotFound)>]
    member this.GetDevUsers() : Task<IActionResult> = task {
        if not isDevMode then
            return this.NotFound() :> IActionResult
        else
            let! users = userRepository.GetAllAsync 0 100

            let devUsers =
                users
                |> List.map (fun user -> {
                    Id = user.State.Id.Value.ToString()
                    Name = user.State.Name
                    Email = user.State.Email
                })

            // Return as ContentResult to avoid chunked encoding issues
            let json = JsonSerializer.Serialize(devUsers, jsonOptions)
            return this.Content(json, "application/json") :> IActionResult
    }