namespace Freetool.Domain

type EventSourcingAggregate<'T> = {
    State: 'T
    UncommittedEvents: IDomainEvent list
}