namespace Freetool.Domain

open System
open Freetool.Domain.ValueObjects

type IDomainEvent =
    abstract member OccurredAt: DateTime
    abstract member EventId: Guid
    abstract member UserId: UserId