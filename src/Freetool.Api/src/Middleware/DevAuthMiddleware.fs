namespace Freetool.Api.Middleware

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System.Threading.Tasks
open System.Diagnostics
open Freetool.Api.Auth
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects
open Freetool.Api.Tracing

type DevAuthMiddleware(next: RequestDelegate) =

    let DEV_USER_ID_HEADER = "X-Dev-User-Id"

    let extractHeader (headerKey: string) (context: HttpContext) : string option =
        match context.Request.Headers.TryGetValue headerKey with
        | true, values when values.Count > 0 ->
            let value = values.[0]

            if System.String.IsNullOrWhiteSpace value then
                None
            else
                Some value
        | _ -> None

    member _.InvokeAsync(context: HttpContext) : Task = task {
        let currentActivity = Option.ofObj Activity.Current
        let path = context.Request.Path.Value

        // Allow development helpers and OpenAPI docs without authentication.
        if
            not (isNull path)
            && (path.StartsWith("/dev/")
                || path.StartsWith("/openapi", System.StringComparison.OrdinalIgnoreCase))
        then
            do! next.Invoke context
        else
            let userIdOption = extractHeader DEV_USER_ID_HEADER context

            match userIdOption with
            | None ->
                Tracing.addAttribute currentActivity "dev.auth.error" "missing_user_id_header"
                Tracing.addAttribute currentActivity "dev.auth.header" DEV_USER_ID_HEADER
                Tracing.setSpanStatus currentActivity false (Some "Missing X-Dev-User-Id header")

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status401Unauthorized
                        "missing_dev_user_id_header"
                        "Unauthorized"
                        $"Missing {DEV_USER_ID_HEADER} header."
                        [ "header", DEV_USER_ID_HEADER ]
            | Some userIdStr ->
                match System.Guid.TryParse userIdStr with
                | false, _ ->
                    Tracing.addAttribute currentActivity "dev.auth.error" "invalid_user_id_format"
                    Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                    Tracing.setSpanStatus currentActivity false (Some "Invalid user ID format")

                    do!
                        ProblemResponses.write
                            context
                            StatusCodes.Status401Unauthorized
                            "invalid_dev_user_id"
                            "Unauthorized"
                            "Invalid user ID format."
                            []
                | true, guid ->
                    let userId = UserId.FromGuid guid
                    let userRepository = context.RequestServices.GetRequiredService<IUserRepository>()
                    let! userOption = userRepository.GetByIdAsync userId

                    match userOption with
                    | None ->
                        Tracing.addAttribute currentActivity "dev.auth.error" "user_not_found"
                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.setSpanStatus currentActivity false (Some "User not found")

                        do!
                            ProblemResponses.write
                                context
                                StatusCodes.Status401Unauthorized
                                "dev_user_not_found"
                                "Unauthorized"
                                "User not found."
                                []
                    | Some user ->
                        context.Items.["UserId"] <- user.State.Id

                        RequestUserContext.set context {
                            UserId = Some user.State.Id
                            Name = Some user.State.Name
                            Email = user.State.Email
                            Profile = user.State.ProfilePicUrl
                            GroupKeys = []
                            AuthenticationSource = "development"
                        }

                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.addAttribute currentActivity "user.id" (user.State.Id.Value.ToString())
                        Tracing.addAttribute currentActivity "dev.auth.success" "true"
                        Tracing.setSpanStatus currentActivity true None
                        do! next.Invoke context
    }