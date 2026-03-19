namespace Freetool.Api.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Freetool.Application.Interfaces
open Freetool.Application.DTOs
open Freetool.Application.Services
open Freetool.Domain.Entities

[<ApiController>]
[<Route("audit")>]
type AuditController(eventRepository: IEventRepository, eventEnhancementService: IEventEnhancementService) =
    inherit ControllerBase()

    let toOptionString (value: string) =
        if String.IsNullOrWhiteSpace(value) then
            None
        else
            Some value

    let toOptionDate (value: Nullable<DateTime>) =
        if value.HasValue then Some value.Value else None

    let toOptionInt (value: Nullable<int>) =
        if value.HasValue then Some value.Value else None

    member private this.EnhancePagedEvents(result: PagedResult<EventData>) : Task<IActionResult> = task {
        let enhancementTasks =
            result.Items
            |> List.map (fun event -> eventEnhancementService.EnhanceEventAsync event)
            |> List.toArray

        let! enhancedItemsArray = Task.WhenAll(enhancementTasks)
        let enhancedItems = enhancedItemsArray |> Array.toList

        let enhancedResult = {
            Items = enhancedItems
            TotalCount = result.TotalCount
            Skip = result.Skip
            Take = result.Take
        }

        return this.Ok(enhancedResult) :> IActionResult
    }

    [<HttpGet("events")>]
    [<ProducesResponseType(typeof<PagedResult<EnhancedEventData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    member this.GetAllEvents
        (
            [<FromQuery>] userId: string,
            [<FromQuery>] eventType: string,
            [<FromQuery>] entityType: string,
            [<FromQuery>] fromDate: Nullable<DateTime>,
            [<FromQuery>] toDate: Nullable<DateTime>,
            [<FromQuery>] skip: Nullable<int>,
            [<FromQuery>] take: Nullable<int>
        ) : Task<IActionResult> =
        task {
            let filterDto: EventFilterDTO = {
                UserId = toOptionString userId
                EventType = toOptionString eventType
                EntityType = toOptionString entityType
                FromDate = toOptionDate fromDate
                ToDate = toOptionDate toDate
                Skip = toOptionInt skip
                Take = toOptionInt take
            }

            match EventFilterValidator.validate filterDto with
            | Ok filter ->
                let! result = eventRepository.GetEventsAsync filter
                return! this.EnhancePagedEvents(result)
            | Error errors -> return this.BadRequest errors :> IActionResult
        }

    [<HttpGet("app/{appId}/events")>]
    [<ProducesResponseType(typeof<PagedResult<EnhancedEventData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    member this.GetAppEvents
        (
            appId: string,
            [<FromQuery>] fromDate: Nullable<DateTime>,
            [<FromQuery>] toDate: Nullable<DateTime>,
            [<FromQuery>] skip: Nullable<int>,
            [<FromQuery>] take: Nullable<int>,
            [<FromQuery>] includeRunEvents: Nullable<bool>
        ) : Task<IActionResult> =
        task {
            let includeRuns =
                if includeRunEvents.HasValue then
                    Some includeRunEvents.Value
                else
                    None

            let filterDto: AppEventFilterDTO = {
                AppId = appId
                FromDate = toOptionDate fromDate
                ToDate = toOptionDate toDate
                Skip = toOptionInt skip
                Take = toOptionInt take
                IncludeRunEvents = includeRuns
            }

            match EventFilterValidator.validateAppFilter filterDto with
            | Ok filter ->
                let! result = eventRepository.GetEventsByAppIdAsync filter
                return! this.EnhancePagedEvents(result)
            | Error errors -> return this.BadRequest errors :> IActionResult
        }

    [<HttpGet("user/{userId}/events")>]
    [<ProducesResponseType(typeof<PagedResult<EnhancedEventData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    member this.GetUserEvents
        (
            userId: string,
            [<FromQuery>] fromDate: Nullable<DateTime>,
            [<FromQuery>] toDate: Nullable<DateTime>,
            [<FromQuery>] skip: Nullable<int>,
            [<FromQuery>] take: Nullable<int>
        ) : Task<IActionResult> =
        task {
            let filterDto: UserEventFilterDTO = {
                UserId = userId
                FromDate = toOptionDate fromDate
                ToDate = toOptionDate toDate
                Skip = toOptionInt skip
                Take = toOptionInt take
            }

            match EventFilterValidator.validateUserFilter filterDto with
            | Ok filter ->
                let! result = eventRepository.GetEventsByUserIdAsync filter
                return! this.EnhancePagedEvents(result)
            | Error errors -> return this.BadRequest errors :> IActionResult
        }

    [<HttpGet("dashboard/{dashboardId}/events")>]
    [<ProducesResponseType(typeof<PagedResult<EnhancedEventData>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(StatusCodes.Status400BadRequest)>]
    member this.GetDashboardEvents
        (
            dashboardId: string,
            [<FromQuery>] fromDate: Nullable<DateTime>,
            [<FromQuery>] toDate: Nullable<DateTime>,
            [<FromQuery>] skip: Nullable<int>,
            [<FromQuery>] take: Nullable<int>
        ) : Task<IActionResult> =
        task {
            let filterDto: DashboardEventFilterDTO = {
                DashboardId = dashboardId
                FromDate = toOptionDate fromDate
                ToDate = toOptionDate toDate
                Skip = toOptionInt skip
                Take = toOptionInt take
            }

            match EventFilterValidator.validateDashboardFilter filterDto with
            | Ok filter ->
                let! result = eventRepository.GetEventsByDashboardIdAsync filter
                return! this.EnhancePagedEvents(result)
            | Error errors -> return this.BadRequest errors :> IActionResult
        }