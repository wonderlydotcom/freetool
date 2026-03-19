namespace Freetool.Api.Tracing

open System

[<AttributeUsage(AttributeTargets.Field)>]
type SpanNameAttribute(spanName: string) =
    inherit Attribute()
    member this.SpanName = spanName

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type TraceAttribute(attributeName: string) =
    inherit Attribute()
    member this.AttributeName = attributeName

[<AttributeUsage(AttributeTargets.Field)>]
type OperationTypeAttribute(operationType: string) =
    inherit Attribute()
    member this.OperationType = operationType

[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field)>]
type SensitiveAttribute() =
    inherit Attribute()