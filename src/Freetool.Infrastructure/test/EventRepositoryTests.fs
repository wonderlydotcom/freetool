module Freetool.Infrastructure.Tests.EventRepositoryTests

open System
open Xunit
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Infrastructure.Database
open Freetool.Infrastructure.Database.Repositories

let private createContext () =
    let services = ServiceCollection()

    services.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase($"EventRepositoryTests_{Guid.NewGuid()}") |> ignore)
    |> ignore

    let provider = services.BuildServiceProvider()
    provider.GetRequiredService<FreetoolDbContext>()

let private createEvent
    (eventType: EventType)
    (entityType: EntityType)
    (entityId: string)
    (userId: UserId)
    (occurredAt: DateTime)
    =
    {
        Id = Guid.NewGuid()
        EventId = Guid.NewGuid().ToString()
        EventType = eventType
        EntityType = entityType
        EntityId = entityId
        EventData = "{}"
        OccurredAt = occurredAt
        CreatedAt = occurredAt
        UserId = userId
    }

let private createRun (runId: RunId) (appId: AppId) (createdAt: DateTime) = {
    Id = runId
    AppId = appId
    Status = RunStatus.Pending
    InputValues = []
    ExecutableRequest = None
    ExecutedSql = None
    Response = None
    ErrorMessage = None
    StartedAt = None
    CompletedAt = None
    CreatedAt = createdAt
    IsDeleted = false
}

[<Fact>]
let ``GetEventsByUserIdAsync returns only matching user's events with pagination`` () = task {
    use context = createContext ()
    let repository = EventRepository(context) :> IEventRepository

    let targetUserId = UserId.NewId()
    let otherUserId = UserId.NewId()

    let events = [
        createEvent
            (EventType.UserEvents UserCreatedEvent)
            EntityType.User
            (targetUserId.Value.ToString())
            targetUserId
            (DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.UserEvents UserUpdatedEvent)
            EntityType.User
            (targetUserId.Value.ToString())
            targetUserId
            (DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.UserEvents UserDeletedEvent)
            EntityType.User
            (otherUserId.Value.ToString())
            otherUserId
            (DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc))
    ]

    context.Events.AddRange(events)
    let! _ = context.SaveChangesAsync()
    ()

    let filter: UserEventFilter = {
        UserId = targetUserId
        FromDate = None
        ToDate = None
        Skip = 0
        Take = 1
    }

    let! result = repository.GetEventsByUserIdAsync(filter)

    Assert.Equal(2, result.TotalCount)
    Assert.Single(result.Items) |> ignore
    Assert.Equal(targetUserId, result.Items.Head.UserId)
    Assert.Equal(EventType.UserEvents UserUpdatedEvent, result.Items.Head.EventType)
}

[<Fact>]
let ``GetEventsByAppIdAsync includes app and run events when includeRunEvents is true`` () = task {
    use context = createContext ()
    let repository = EventRepository(context) :> IEventRepository

    let userId = UserId.NewId()
    let appId = AppId.NewId()
    let otherAppId = AppId.NewId()
    let runIdForApp = RunId.NewId()
    let runIdForOtherApp = RunId.NewId()

    context.Runs.Add(createRun runIdForApp appId (DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)))
    context.Runs.Add(createRun runIdForOtherApp otherAppId (DateTime(2026, 1, 2, 0, 0, 1, DateTimeKind.Utc)))

    let events = [
        createEvent
            (EventType.AppEvents AppUpdatedEvent)
            EntityType.App
            (appId.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.RunEvents RunStatusChangedEvent)
            EntityType.Run
            (runIdForApp.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.RunEvents RunStatusChangedEvent)
            EntityType.Run
            (runIdForOtherApp.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.AppEvents AppUpdatedEvent)
            EntityType.App
            (otherAppId.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc))
    ]

    context.Events.AddRange(events)
    let! _ = context.SaveChangesAsync()
    ()

    let filter: AppEventFilter = {
        AppId = appId
        FromDate = None
        ToDate = None
        Skip = 0
        Take = 50
        IncludeRunEvents = true
    }

    let! result = repository.GetEventsByAppIdAsync(filter)

    Assert.Equal(2, result.TotalCount)
    Assert.Equal(2, result.Items.Length)
    Assert.Contains(result.Items, fun e -> e.EntityType = EntityType.App && e.EntityId = appId.Value.ToString())

    Assert.Contains(result.Items, fun e -> e.EntityType = EntityType.Run && e.EntityId = runIdForApp.Value.ToString())
}

[<Fact>]
let ``GetEventsByAppIdAsync excludes run events when includeRunEvents is false`` () = task {
    use context = createContext ()
    let repository = EventRepository(context) :> IEventRepository

    let userId = UserId.NewId()
    let appId = AppId.NewId()
    let runIdForApp = RunId.NewId()

    context.Runs.Add(createRun runIdForApp appId (DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)))

    let events = [
        createEvent
            (EventType.AppEvents AppUpdatedEvent)
            EntityType.App
            (appId.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc))
        createEvent
            (EventType.RunEvents RunStatusChangedEvent)
            EntityType.Run
            (runIdForApp.Value.ToString())
            userId
            (DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc))
    ]

    context.Events.AddRange(events)
    let! _ = context.SaveChangesAsync()
    ()

    let filter: AppEventFilter = {
        AppId = appId
        FromDate = None
        ToDate = None
        Skip = 0
        Take = 50
        IncludeRunEvents = false
    }

    let! result = repository.GetEventsByAppIdAsync(filter)

    Assert.Single(result.Items) |> ignore
    Assert.Equal(EntityType.App, result.Items.Head.EntityType)
}