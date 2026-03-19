namespace Freetool.Application.DTOs

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open System
open System.ComponentModel.DataAnnotations

[<CLIMutable>]
type EventFilterDTO = {
    UserId: string option
    EventType: string option
    EntityType: string option
    FromDate: DateTime option
    ToDate: DateTime option

    [<Range(0, 2147483647)>]
    Skip: int option

    [<Range(0, 100)>]
    Take: int option
}

type EventFilter = {
    UserId: UserId option
    EventType: EventType option
    EntityType: EntityType option
    FromDate: DateTime option
    ToDate: DateTime option
    Skip: int
    Take: int
}

[<CLIMutable>]
type AppEventFilterDTO = {
    AppId: string
    FromDate: DateTime option
    ToDate: DateTime option

    [<Range(0, 2147483647)>]
    Skip: int option

    [<Range(0, 100)>]
    Take: int option
    IncludeRunEvents: bool option
}

type AppEventFilter = {
    AppId: AppId
    FromDate: DateTime option
    ToDate: DateTime option
    Skip: int
    Take: int
    IncludeRunEvents: bool
}

[<CLIMutable>]
type DashboardEventFilterDTO = {
    DashboardId: string
    FromDate: DateTime option
    ToDate: DateTime option

    [<Range(0, 2147483647)>]
    Skip: int option

    [<Range(0, 100)>]
    Take: int option
}

type DashboardEventFilter = {
    DashboardId: DashboardId
    FromDate: DateTime option
    ToDate: DateTime option
    Skip: int
    Take: int
}

[<CLIMutable>]
type UserEventFilterDTO = {
    UserId: string
    FromDate: DateTime option
    ToDate: DateTime option

    [<Range(0, 2147483647)>]
    Skip: int option

    [<Range(0, 100)>]
    Take: int option
}

type UserEventFilter = {
    UserId: UserId
    FromDate: DateTime option
    ToDate: DateTime option
    Skip: int
    Take: int
}

module EventFilterValidator =
    let private validatePagination (skip: int option) (take: int option) (errors: string list) =
        let mutable validationErrors = errors

        match skip with
        | Some skipValue when skipValue < 0 ->
            validationErrors <- "Skip must be greater than or equal to 0" :: validationErrors
        | _ -> ()

        match take with
        | Some takeValue when takeValue < 0 || takeValue > 100 ->
            validationErrors <- "Take must be between 0 and 100" :: validationErrors
        | _ -> ()

        validationErrors

    let private validateDateRange (fromDate: DateTime option) (toDate: DateTime option) (errors: string list) =
        match fromDate, toDate with
        | Some fromDateValue, Some toDateValue when fromDateValue > toDateValue ->
            "FromDate must be less than or equal to ToDate" :: errors
        | _ -> errors

    let validate (dto: EventFilterDTO) : Result<EventFilter, string list> =
        let mutable errors = []
        let mutable userId = None
        let mutable eventType = None
        let mutable entityType = None

        // Validate UserId
        match dto.UserId with
        | Some userIdStr ->
            match Guid.TryParse(userIdStr) with
            | true, guid -> userId <- Some(UserId.FromGuid guid)
            | false, _ -> errors <- "Invalid UserId format - must be a valid GUID" :: errors
        | None -> ()

        // Validate EventType
        match dto.EventType with
        | Some eventTypeStr ->
            match EventTypeConverter.fromString eventTypeStr with
            | Some et -> eventType <- Some et
            | None -> errors <- $"Invalid EventType: {eventTypeStr}" :: errors
        | None -> ()

        // Validate EntityType
        match dto.EntityType with
        | Some entityTypeStr ->
            match EntityTypeConverter.fromString entityTypeStr with
            | Some et -> entityType <- Some et
            | None -> errors <- $"Invalid EntityType: {entityTypeStr}" :: errors
        | None -> ()

        errors <- validatePagination dto.Skip dto.Take errors
        errors <- validateDateRange dto.FromDate dto.ToDate errors

        if List.isEmpty errors then
            Ok {
                UserId = userId
                EventType = eventType
                EntityType = entityType
                FromDate = dto.FromDate
                ToDate = dto.ToDate
                Skip = dto.Skip |> Option.defaultValue 0
                Take = dto.Take |> Option.defaultValue 50
            }
        else
            Error(List.rev errors)

    let validateAppFilter (dto: AppEventFilterDTO) : Result<AppEventFilter, string list> =
        let mutable errors = []
        let mutable appId = None

        match Guid.TryParse(dto.AppId) with
        | true, guid -> appId <- Some(AppId.FromGuid guid)
        | false, _ -> errors <- "Invalid AppId format - must be a valid GUID" :: errors

        errors <- validatePagination dto.Skip dto.Take errors
        errors <- validateDateRange dto.FromDate dto.ToDate errors

        if List.isEmpty errors then
            Ok {
                AppId = appId.Value
                FromDate = dto.FromDate
                ToDate = dto.ToDate
                Skip = dto.Skip |> Option.defaultValue 0
                Take = dto.Take |> Option.defaultValue 50
                IncludeRunEvents = dto.IncludeRunEvents |> Option.defaultValue true
            }
        else
            Error(List.rev errors)

    let validateDashboardFilter (dto: DashboardEventFilterDTO) : Result<DashboardEventFilter, string list> =
        let mutable errors = []
        let mutable dashboardId = None

        match Guid.TryParse(dto.DashboardId) with
        | true, guid -> dashboardId <- Some(DashboardId.FromGuid guid)
        | false, _ -> errors <- "Invalid DashboardId format - must be a valid GUID" :: errors

        errors <- validatePagination dto.Skip dto.Take errors
        errors <- validateDateRange dto.FromDate dto.ToDate errors

        if List.isEmpty errors then
            Ok {
                DashboardId = dashboardId.Value
                FromDate = dto.FromDate
                ToDate = dto.ToDate
                Skip = dto.Skip |> Option.defaultValue 0
                Take = dto.Take |> Option.defaultValue 50
            }
        else
            Error(List.rev errors)

    let validateUserFilter (dto: UserEventFilterDTO) : Result<UserEventFilter, string list> =
        let mutable errors = []
        let mutable userId = None

        match Guid.TryParse(dto.UserId) with
        | true, guid -> userId <- Some(UserId.FromGuid guid)
        | false, _ -> errors <- "Invalid UserId format - must be a valid GUID" :: errors

        errors <- validatePagination dto.Skip dto.Take errors
        errors <- validateDateRange dto.FromDate dto.ToDate errors

        if List.isEmpty errors then
            Ok {
                UserId = userId.Value
                FromDate = dto.FromDate
                ToDate = dto.ToDate
                Skip = dto.Skip |> Option.defaultValue 0
                Take = dto.Take |> Option.defaultValue 50
            }
        else
            Error(List.rev errors)

[<CLIMutable>]
type EnhancedEventData = {
    Id: Guid
    EventId: string
    EventType: EventType
    EntityType: EntityType
    EntityId: string
    EntityName: string
    EventData: string
    OccurredAt: DateTime
    CreatedAt: DateTime
    UserId: UserId
    UserName: string
    EventSummary: string
}