module Freetool.Infrastructure.Tests.AuditControllerTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Xunit
open Freetool.Api.Controllers
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Application.Services
open Freetool.Domain
open Freetool.Domain.Entities

type MockEventRepository() =
    let mutable lastFilter: EventFilter option = None
    let mutable lastAppFilter: AppEventFilter option = None
    let mutable lastDashboardFilter: DashboardEventFilter option = None
    let mutable lastUserFilter: UserEventFilter option = None

    member _.LastFilter = lastFilter
    member _.LastAppFilter = lastAppFilter
    member _.LastDashboardFilter = lastDashboardFilter
    member _.LastUserFilter = lastUserFilter

    interface IEventRepository with
        member _.SaveEventAsync(_event: IDomainEvent) = Task.FromResult(())

        member _.CommitAsync() = Task.FromResult(())

        member _.GetEventsAsync(filter: EventFilter) = task {
            lastFilter <- Some filter

            return {
                Items = []
                TotalCount = 0
                Skip = filter.Skip
                Take = filter.Take
            }
        }

        member _.GetEventsByAppIdAsync(filter: AppEventFilter) = task {
            lastAppFilter <- Some filter

            return {
                Items = []
                TotalCount = 0
                Skip = filter.Skip
                Take = filter.Take
            }
        }

        member _.GetEventsByDashboardIdAsync(filter: DashboardEventFilter) = task {
            lastDashboardFilter <- Some filter

            return {
                Items = []
                TotalCount = 0
                Skip = filter.Skip
                Take = filter.Take
            }
        }

        member _.GetEventsByUserIdAsync(filter: UserEventFilter) = task {
            lastUserFilter <- Some filter

            return {
                Items = []
                TotalCount = 0
                Skip = filter.Skip
                Take = filter.Take
            }
        }

type MockEventEnhancementService() =
    interface IEventEnhancementService with
        member _.EnhanceEventAsync(event: EventData) =
            Task.FromResult(
                {
                    Id = event.Id
                    EventId = event.EventId
                    EventType = event.EventType
                    EntityType = event.EntityType
                    EntityId = event.EntityId
                    EntityName = "Test"
                    EventData = event.EventData
                    OccurredAt = event.OccurredAt
                    CreatedAt = event.CreatedAt
                    UserId = event.UserId
                    UserName = "Test User"
                    EventSummary = "Test summary"
                }
            )

[<Fact>]
let ``GetAllEvents passes skip and take query params to repository filter`` () : Task = task {
    let eventRepository = MockEventRepository()
    let controller = AuditController(eventRepository, MockEventEnhancementService())

    let! result = controller.GetAllEvents(null, null, null, Nullable(), Nullable(), Nullable 2000, Nullable 50)

    let okResult = Assert.IsType<OkObjectResult>(result)
    let payload = Assert.IsType<PagedResult<EnhancedEventData>>(okResult.Value)

    Assert.Equal(2000, payload.Skip)
    Assert.Equal(50, payload.Take)
    Assert.True(eventRepository.LastFilter.IsSome)
    Assert.Equal(2000, eventRepository.LastFilter.Value.Skip)
    Assert.Equal(50, eventRepository.LastFilter.Value.Take)
}

[<Fact>]
let ``GetAppEvents passes appId and includeRunEvents to repository filter`` () : Task = task {
    let eventRepository = MockEventRepository()
    let controller = AuditController(eventRepository, MockEventEnhancementService())
    let appId = Guid.NewGuid().ToString()

    let! result = controller.GetAppEvents(appId, Nullable(), Nullable(), Nullable 10, Nullable 25, Nullable true)

    let okResult = Assert.IsType<OkObjectResult>(result)
    let payload = Assert.IsType<PagedResult<EnhancedEventData>>(okResult.Value)

    Assert.Equal(10, payload.Skip)
    Assert.Equal(25, payload.Take)
    Assert.True(eventRepository.LastAppFilter.IsSome)
    Assert.Equal(appId, eventRepository.LastAppFilter.Value.AppId.Value.ToString())
    Assert.True(eventRepository.LastAppFilter.Value.IncludeRunEvents)
}

[<Fact>]
let ``GetAppEvents returns bad request for invalid appId`` () : Task = task {
    let eventRepository = MockEventRepository()
    let controller = AuditController(eventRepository, MockEventEnhancementService())

    let! result = controller.GetAppEvents("not-a-guid", Nullable(), Nullable(), Nullable(), Nullable(), Nullable())

    let badRequest = Assert.IsType<BadRequestObjectResult>(result)
    let errors = Assert.IsType<string list>(badRequest.Value)
    Assert.Contains("Invalid AppId format - must be a valid GUID", errors)
}

[<Fact>]
let ``GetUserEvents passes userId to repository filter`` () : Task = task {
    let eventRepository = MockEventRepository()
    let controller = AuditController(eventRepository, MockEventEnhancementService())
    let userId = Guid.NewGuid().ToString()

    let! result = controller.GetUserEvents(userId, Nullable(), Nullable(), Nullable 5, Nullable 40)

    let okResult = Assert.IsType<OkObjectResult>(result)
    let payload = Assert.IsType<PagedResult<EnhancedEventData>>(okResult.Value)

    Assert.Equal(5, payload.Skip)
    Assert.Equal(40, payload.Take)
    Assert.True(eventRepository.LastUserFilter.IsSome)
    Assert.Equal(userId, eventRepository.LastUserFilter.Value.UserId.Value.ToString())
}