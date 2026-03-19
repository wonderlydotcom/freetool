namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Application.DTOs
open Freetool.Domain.Entities

type IEventRepository =
    abstract member SaveEventAsync: event: IDomainEvent -> Task<unit>
    /// Commits pending changes. Use for standalone event saves not part of an aggregate operation.
    abstract member CommitAsync: unit -> Task<unit>
    abstract member GetEventsAsync: filter: EventFilter -> Task<PagedResult<EventData>>
    abstract member GetEventsByAppIdAsync: filter: AppEventFilter -> Task<PagedResult<EventData>>
    abstract member GetEventsByDashboardIdAsync: filter: DashboardEventFilter -> Task<PagedResult<EventData>>
    abstract member GetEventsByUserIdAsync: filter: UserEventFilter -> Task<PagedResult<EventData>>