namespace Freetool.Api.Tracing

open System
open System.Collections.Generic
open System.Diagnostics
open Freetool.Domain

module Tracing =

    let withSpan
        (activitySource: ActivitySource)
        (spanName: string)
        (operation: Activity option -> System.Threading.Tasks.Task<'T>)
        : System.Threading.Tasks.Task<'T> =
        task {
            use activity = activitySource.StartActivity(spanName)
            printfn "Creating span: %s, Activity: %A" spanName (Option.ofObj activity)
            return! operation (Option.ofObj activity)
        }

    let addAttribute (activity: Activity option) (key: string) (value: string) =
        match activity with
        | Some a -> a.SetTag(key, value) |> ignore
        | None -> ()

    let addIntAttribute (activity: Activity option) (key: string) (value: int) =
        match activity with
        | Some a -> a.SetTag(key, value.ToString()) |> ignore
        | None -> ()

    let setSpanStatus (activity: Activity option) (success: bool) (errorMessage: string option) =
        match activity with
        | Some a ->
            if success then
                a.SetStatus(ActivityStatusCode.Ok) |> ignore
            else
                a.SetStatus(ActivityStatusCode.Error, defaultArg errorMessage "Operation failed")
                |> ignore
        | None -> ()

    let addDomainErrorEvent (activity: Activity option) (error: DomainError) =
        match activity with
        | Some a ->
            let errorType, message =
                match error with
                | ValidationError msg -> "ValidationError", msg
                | NotFound msg -> "NotFound", msg
                | Conflict msg -> "Conflict", msg
                | InvalidOperation msg -> "InvalidOperation", msg

            a.AddEvent(
                ActivityEvent(
                    "domain_error",
                    DateTimeOffset.UtcNow,
                    ActivityTagsCollection [
                        KeyValuePair.Create("error.type", errorType)
                        KeyValuePair.Create("error.message", message)
                    ]
                )
            )
            |> ignore

            a.SetTag("error.type", errorType) |> ignore
            a.SetTag("error.message", message) |> ignore
        | None -> ()

    let addUserAttributes (activity: Activity option) (userId: string option) (email: string option) =
        match activity with
        | Some a ->
            userId |> Option.iter (fun id -> a.SetTag("user.id", id) |> ignore)
            email |> Option.iter (fun e -> a.SetTag("user.email", e) |> ignore)
        | None -> ()

    let addHttpAttributes (activity: Activity option) (method: string) (route: string) (statusCode: int option) =
        match activity with
        | Some a ->
            a.SetTag("http.method", method) |> ignore
            a.SetTag("http.route", route) |> ignore

            statusCode
            |> Option.iter (fun code -> a.SetTag("http.status_code", code.ToString()) |> ignore)
        | None -> ()

    let addPaginationAttributes (activity: Activity option) (skip: int) (take: int) (resultCount: int option) =
        match activity with
        | Some a ->
            a.SetTag("pagination.skip", skip.ToString()) |> ignore
            a.SetTag("pagination.take", take.ToString()) |> ignore

            resultCount
            |> Option.iter (fun count -> a.SetTag("pagination.result_count", count.ToString()) |> ignore)
        | None -> ()