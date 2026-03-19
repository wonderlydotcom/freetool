namespace Freetool.Api.Controllers

open Microsoft.AspNetCore.Mvc
open Freetool.Domain.ValueObjects

[<ApiController>]
type AuthenticatedControllerBase() =
    inherit ControllerBase()

    member this.CurrentUserId: UserId =
        match this.HttpContext.Items.TryGetValue "UserId" with
        | true, userIdObj when userIdObj <> null -> userIdObj :?> UserId
        | _ ->
            invalidOp
                "User not authenticated. This indicates a middleware configuration error - authentication middleware should have rejected unauthenticated requests."

    [<NonAction>]
    member this.TryGetCurrentUserId() : UserId option =
        match this.HttpContext.Items.TryGetValue "UserId" with
        | true, userIdObj when userIdObj <> null -> Some(userIdObj :?> UserId)
        | _ -> None