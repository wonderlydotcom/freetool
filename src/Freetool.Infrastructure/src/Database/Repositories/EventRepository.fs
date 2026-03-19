namespace Freetool.Infrastructure.Database.Repositories

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open System.Text.Json
open System.Text.Json.Serialization
open Freetool.Domain
open Freetool.Domain.Events
open Freetool.Application.Interfaces
open Freetool.Application.DTOs
open Freetool.Domain.Entities
open Freetool.Infrastructure.Database

type EventRepository(context: FreetoolDbContext) =
    let applyDateFilters (query: IQueryable<EventData>) (fromDate: DateTime option) (toDate: DateTime option) =
        query
        |> fun q ->
            match fromDate with
            | Some fromDateValue -> q.Where(fun e -> e.OccurredAt >= fromDateValue)
            | None -> q
        |> fun q ->
            match toDate with
            | Some toDateValue -> q.Where(fun e -> e.OccurredAt <= toDateValue)
            | None -> q

    let toPagedResultAsync
        (query: IQueryable<EventData>)
        (skip: int)
        (take: int)
        : Threading.Tasks.Task<PagedResult<EventData>> =
        task {
            let! totalCount = query.CountAsync()

            let! items = query.OrderByDescending(fun e -> e.OccurredAt).Skip(skip).Take(take).ToListAsync()

            return {
                Items = items |> List.ofSeq
                TotalCount = totalCount
                Skip = skip
                Take = take
            }
        }

    interface IEventRepository with
        member this.SaveEventAsync(event: IDomainEvent) = task {
            let eventTypeName = event.GetType().Name

            let eventType =
                EventTypeConverter.fromString eventTypeName
                |> Option.defaultWith (fun () -> failwith $"Unknown event type: {eventTypeName}")

            let entityType =
                match eventType with
                | UserEvents _ -> EntityType.User
                | AppEvents _ -> EntityType.App
                | DashboardEvents _ -> EntityType.Dashboard
                | ResourceEvents _ -> EntityType.Resource
                | FolderEvents _ -> EntityType.Folder
                | RunEvents _ -> EntityType.Run
                | SpaceEvents _ -> EntityType.Space

            let jsonOptions = JsonSerializerOptions()
            // Note: Do NOT use JsonIgnoreCondition.WhenWritingNull here
            // F# option types serialize as null when None, and if omitted,
            // deserialization will fail with "missing field" errors
            jsonOptions.Converters.Add(JsonFSharpConverter())

            let (entityId, eventData) =
                match event with
                | :? Events.UserCreatedEvent as e -> (e.UserId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.UserUpdatedEvent as e -> (e.UserId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.UserDeletedEvent as e -> (e.UserId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.UserInvitedEvent as e -> (e.UserId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.UserActivatedEvent as e -> (e.UserId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.AppCreatedEvent as e -> (e.AppId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.AppUpdatedEvent as e -> (e.AppId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.AppDeletedEvent as e -> (e.AppId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.AppRestoredEvent as e -> (e.AppId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardCreatedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardUpdatedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardDeletedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardPreparedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardPrepareFailedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardActionExecutedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.DashboardActionFailedEvent as e ->
                    (e.DashboardId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.ResourceCreatedEvent as e ->
                    (e.ResourceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.ResourceUpdatedEvent as e ->
                    (e.ResourceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.ResourceDeletedEvent as e ->
                    (e.ResourceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.ResourceRestoredEvent as e ->
                    (e.ResourceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.FolderCreatedEvent as e -> (e.FolderId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.FolderUpdatedEvent as e -> (e.FolderId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.FolderDeletedEvent as e -> (e.FolderId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.FolderRestoredEvent as e ->
                    (e.FolderId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.RunCreatedEvent as e -> (e.RunId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.RunStatusChangedEvent as e -> (e.RunId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.SpaceCreatedEvent as e -> (e.SpaceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.SpaceUpdatedEvent as e -> (e.SpaceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.SpaceDeletedEvent as e -> (e.SpaceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.SpacePermissionsChangedEvent as e ->
                    (e.SpaceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | :? Events.SpaceDefaultMemberPermissionsChangedEvent as e ->
                    (e.SpaceId.ToString(), JsonSerializer.Serialize(e, jsonOptions))
                | _ -> ("unknown", JsonSerializer.Serialize(event, jsonOptions))

            let eventDataRecord: Entities.EventData = {
                Id = Guid.NewGuid()
                EventId = event.EventId.ToString()
                EventType = eventType
                EntityType = entityType
                EntityId = entityId
                EventData = eventData
                OccurredAt = event.OccurredAt
                CreatedAt = DateTime.UtcNow
                UserId = event.UserId
            }

            context.Events.Add(eventDataRecord) |> ignore
            return ()
        }

        /// Commits any pending changes to the database.
        /// Use this for standalone event saves that don't go through an aggregate repository.
        /// Aggregate repositories (UserRepository, SpaceRepository, etc.) handle their own
        /// SaveChangesAsync calls, so don't call this when saving events as part of an aggregate operation.
        member this.CommitAsync() = task {
            let! _ = context.SaveChangesAsync()
            return ()
        }

        member this.GetEventsAsync(filter: EventFilter) : Threading.Tasks.Task<PagedResult<EventData>> = task {
            let query = context.Events.AsQueryable()

            // Apply filters
            let filteredQuery =
                query
                |> fun q ->
                    match filter.UserId with
                    | Some userId -> q.Where(fun e -> e.UserId = userId)
                    | None -> q
                |> fun q ->
                    match filter.EventType with
                    | Some eventType -> q.Where(fun e -> e.EventType = eventType)
                    | None -> q
                |> fun q ->
                    match filter.EntityType with
                    | Some entityType -> q.Where(fun e -> e.EntityType = entityType)
                    | None -> q
                |> fun q -> applyDateFilters q filter.FromDate filter.ToDate

            return! toPagedResultAsync filteredQuery filter.Skip filter.Take
        }

        member this.GetEventsByAppIdAsync(filter: AppEventFilter) : Threading.Tasks.Task<PagedResult<EventData>> = task {
            let appIdValue = filter.AppId.Value.ToString()
            let query = context.Events.AsQueryable()

            let appEventsQuery =
                query.Where(fun e -> e.EntityType = EntityType.App && e.EntityId = appIdValue)

            let! runIdsForApp =
                if filter.IncludeRunEvents then
                    task {
                        let! runIds =
                            context.Runs.Where(fun r -> r.AppId = filter.AppId).Select(fun r -> r.Id).ToListAsync()

                        return
                            runIds
                            |> Seq.map (fun runId -> runId.Value.ToString())
                            |> System.Collections.Generic.List<string>
                    }
                else
                    task { return System.Collections.Generic.List<string>() }

            let filteredQuery =
                if filter.IncludeRunEvents then
                    query.Where(fun e ->
                        (e.EntityType = EntityType.App && e.EntityId = appIdValue)
                        || (e.EntityType = EntityType.Run && runIdsForApp.Contains(e.EntityId)))
                else
                    appEventsQuery
                |> fun q -> applyDateFilters q filter.FromDate filter.ToDate

            return! toPagedResultAsync filteredQuery filter.Skip filter.Take
        }

        member this.GetEventsByDashboardIdAsync
            (filter: DashboardEventFilter)
            : Threading.Tasks.Task<PagedResult<EventData>> =
            task {
                let dashboardIdValue = filter.DashboardId.Value.ToString()
                let query = context.Events.AsQueryable()

                let filteredQuery =
                    query.Where(fun e -> e.EntityType = EntityType.Dashboard && e.EntityId = dashboardIdValue)
                    |> fun q -> applyDateFilters q filter.FromDate filter.ToDate

                return! toPagedResultAsync filteredQuery filter.Skip filter.Take
            }

        member this.GetEventsByUserIdAsync(filter: UserEventFilter) : Threading.Tasks.Task<PagedResult<EventData>> = task {
            let query =
                context.Events.Where(fun e -> e.UserId = filter.UserId)
                |> fun q -> applyDateFilters q filter.FromDate filter.ToDate

            return! toPagedResultAsync query filter.Skip filter.Take
        }