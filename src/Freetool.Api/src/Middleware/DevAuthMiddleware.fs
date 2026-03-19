namespace Freetool.Api.Middleware

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System.Threading.Tasks
open System.Diagnostics
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

        // Allow /dev/* endpoints without authentication (needed to fetch user list)
        if not (isNull path) && path.StartsWith("/dev/") then
            do! next.Invoke context
        else
            let userIdOption = extractHeader DEV_USER_ID_HEADER context

            match userIdOption with
            | None ->
                Tracing.addAttribute currentActivity "dev.auth.error" "missing_user_id_header"
                Tracing.addAttribute currentActivity "dev.auth.header" DEV_USER_ID_HEADER
                Tracing.setSpanStatus currentActivity false (Some "Missing X-Dev-User-Id header")
                context.Response.StatusCode <- 401
                do! context.Response.WriteAsync $"Unauthorized: Missing {DEV_USER_ID_HEADER} header"
            | Some userIdStr ->
                match System.Guid.TryParse userIdStr with
                | false, _ ->
                    Tracing.addAttribute currentActivity "dev.auth.error" "invalid_user_id_format"
                    Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                    Tracing.setSpanStatus currentActivity false (Some "Invalid user ID format")
                    context.Response.StatusCode <- 401
                    do! context.Response.WriteAsync "Unauthorized: Invalid user ID format"
                | true, guid ->
                    let userId = UserId.FromGuid guid
                    let userRepository = context.RequestServices.GetRequiredService<IUserRepository>()
                    let! userOption = userRepository.GetByIdAsync userId

                    match userOption with
                    | None ->
                        Tracing.addAttribute currentActivity "dev.auth.error" "user_not_found"
                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.setSpanStatus currentActivity false (Some "User not found")
                        context.Response.StatusCode <- 401
                        do! context.Response.WriteAsync "Unauthorized: User not found"
                    | Some user ->
                        context.Items.["UserId"] <- user.State.Id
                        Tracing.addAttribute currentActivity "dev.auth.user_id" userIdStr
                        Tracing.addAttribute currentActivity "user.id" (user.State.Id.Value.ToString())
                        Tracing.addAttribute currentActivity "dev.auth.success" "true"
                        Tracing.setSpanStatus currentActivity true None
                        do! next.Invoke context
    }