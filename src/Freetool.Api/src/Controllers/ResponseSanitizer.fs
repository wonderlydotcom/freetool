namespace Freetool.Api.Controllers

open Freetool.Domain.Entities
open Freetool.Application.DTOs
open Freetool.Application.Services

module ResponseSanitizer =
    let sanitizeResource (resource: ResourceData) : ResourceData = {
        resource with
            Headers = AuthorizationHeaderRedaction.redactKeyValuePairs resource.Headers
            DatabasePassword = None
    }

    let sanitizeResources (pagedResources: PagedResult<ResourceData>) : PagedResult<ResourceData> = {
        pagedResources with
            Items = pagedResources.Items |> List.map sanitizeResource
    }

    let sanitizeApp (app: AppData) : AppData = {
        app with
            Headers = AuthorizationHeaderRedaction.redactKeyValuePairs app.Headers
    }

    let sanitizeApps (pagedApps: PagedResult<AppData>) : PagedResult<AppData> = {
        pagedApps with
            Items = pagedApps.Items |> List.map sanitizeApp
    }

    let sanitizeRun (run: RunData) : RunData =
        let redactedExecutableRequest =
            run.ExecutableRequest
            |> Option.map (fun request -> {
                request with
                    Headers = AuthorizationHeaderRedaction.redactTupleHeaders request.Headers
            })

        {
            run with
                ExecutableRequest = redactedExecutableRequest
        }