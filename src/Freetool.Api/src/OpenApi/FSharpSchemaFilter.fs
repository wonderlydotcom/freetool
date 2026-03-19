namespace Freetool.Api.OpenApi

open Microsoft.OpenApi.Models
open Microsoft.OpenApi.Any
open Swashbuckle.AspNetCore.SwaggerGen
open System.Collections.Generic
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type FSharpUnionSchemaFilter() =
    interface ISchemaFilter with
        member _.Apply(schema: OpenApiSchema, context: SchemaFilterContext) =
            // Handle F# int types - ensure they're treated as primitive integers
            if context.Type = typeof<int> then
                schema.Type <- "integer"
                schema.Format <- "int32"
                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle ID value objects - convert them to simple UUID strings
            elif
                context.Type = typeof<UserId>
                || context.Type = typeof<AppId>
                || context.Type = typeof<DashboardId>
                || context.Type = typeof<FolderId>
                || context.Type = typeof<ResourceId>
                || context.Type = typeof<RunId>
                || context.Type = typeof<SpaceId>
            then
                schema.Type <- "string"
                schema.Format <- "uuid"
                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle string-based value objects - convert them to simple strings
            elif
                context.Type = typeof<AppName>
                || context.Type = typeof<BaseUrl>
                || context.Type = typeof<DatabaseAuthScheme>
                || context.Type = typeof<DatabaseHost>
                || context.Type = typeof<DatabaseName>
                || context.Type = typeof<DatabasePassword>
                || context.Type = typeof<DatabaseUsername>
                || context.Type = typeof<Email>
                || context.Type = typeof<FolderName>
                || context.Type = typeof<ResourceDescription>
                || context.Type = typeof<ResourceName>
                || context.Type = typeof<Url>
                || context.Type = typeof<ActionId>
                || context.Type = typeof<InputTitle>
            then
                schema.Type <- "string"
                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle F# option types - convert them to nullable types
            elif
                context.Type.IsGenericType
                && context.Type.GetGenericTypeDefinition().Name.Contains("FSharpOption")
            then
                let innerType = context.Type.GetGenericArguments().[0]

                if innerType = typeof<string> then
                    schema.Type <- "string"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle int option - convert to nullable integer
                elif innerType = typeof<int> then
                    schema.Type <- "integer"
                    schema.Format <- "int32"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle DatabasePort option - convert to nullable integer
                elif innerType = typeof<DatabasePort> then
                    schema.Type <- "integer"
                    schema.Format <- "int32"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle DateTime option - convert to nullable datetime
                elif innerType = typeof<System.DateTime> then
                    schema.Type <- "string"
                    schema.Format <- "date-time"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle FolderId option - convert to nullable UUID string
                elif innerType = typeof<FolderId> then
                    schema.Type <- "string"
                    schema.Format <- "uuid"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle option value objects that serialize to strings
                elif
                    innerType = typeof<BaseUrl>
                    || innerType = typeof<DatabaseAuthScheme>
                    || innerType = typeof<DatabaseHost>
                    || innerType = typeof<DatabaseName>
                    || innerType = typeof<DatabasePassword>
                    || innerType = typeof<DatabaseUsername>
                    || innerType = typeof<ResourceKind>
                then
                    schema.Type <- "string"
                    schema.Nullable <- true
                    schema.Properties <- null
                    schema.AdditionalProperties <- null
                    schema.Required <- null
                // Handle string list option types - convert them to nullable string arrays
                elif
                    innerType.IsGenericType
                    && innerType.GetGenericTypeDefinition().Name.Contains("FSharpList")
                then
                    let listItemType = innerType.GetGenericArguments().[0]

                    if listItemType = typeof<string> then
                        schema.Type <- "array"
                        schema.Items <- OpenApiSchema(Type = "string")
                        schema.Nullable <- true
                        schema.Properties <- null
                        schema.AdditionalProperties <- null
                        schema.Required <- null

            // Handle EventType union
            elif context.Type = typeof<EventType> then
                schema.Type <- "string"

                schema.Enum <- [|
                    OpenApiString("UserCreatedEvent") :> IOpenApiAny
                    OpenApiString("UserUpdatedEvent") :> IOpenApiAny
                    OpenApiString("UserDeletedEvent") :> IOpenApiAny
                    OpenApiString("AppCreatedEvent") :> IOpenApiAny
                    OpenApiString("AppUpdatedEvent") :> IOpenApiAny
                    OpenApiString("AppDeletedEvent") :> IOpenApiAny
                    OpenApiString("DashboardCreatedEvent") :> IOpenApiAny
                    OpenApiString("DashboardUpdatedEvent") :> IOpenApiAny
                    OpenApiString("DashboardDeletedEvent") :> IOpenApiAny
                    OpenApiString("DashboardPreparedEvent") :> IOpenApiAny
                    OpenApiString("DashboardPrepareFailedEvent") :> IOpenApiAny
                    OpenApiString("DashboardActionExecutedEvent") :> IOpenApiAny
                    OpenApiString("DashboardActionFailedEvent") :> IOpenApiAny
                    OpenApiString("ResourceCreatedEvent") :> IOpenApiAny
                    OpenApiString("ResourceUpdatedEvent") :> IOpenApiAny
                    OpenApiString("ResourceDeletedEvent") :> IOpenApiAny
                    OpenApiString("FolderCreatedEvent") :> IOpenApiAny
                    OpenApiString("FolderUpdatedEvent") :> IOpenApiAny
                    OpenApiString("FolderDeletedEvent") :> IOpenApiAny
                    OpenApiString("RunCreatedEvent") :> IOpenApiAny
                    OpenApiString("RunStatusChangedEvent") :> IOpenApiAny
                    OpenApiString("SpaceCreatedEvent") :> IOpenApiAny
                    OpenApiString("SpaceUpdatedEvent") :> IOpenApiAny
                    OpenApiString("SpaceDeletedEvent") :> IOpenApiAny
                |]

                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle HttpMethod union
            elif context.Type = typeof<HttpMethod> then
                schema.Type <- "string"

                schema.Enum <- [|
                    OpenApiString("DELETE") :> IOpenApiAny
                    OpenApiString("GET") :> IOpenApiAny
                    OpenApiString("PATCH") :> IOpenApiAny
                    OpenApiString("POST") :> IOpenApiAny
                    OpenApiString("PUT") :> IOpenApiAny
                |]

                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle ResourceKind union
            elif context.Type = typeof<ResourceKind> then
                schema.Type <- "string"

                schema.Enum <- [| OpenApiString("HTTP") :> IOpenApiAny; OpenApiString("SQL") :> IOpenApiAny |]

                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

            // Handle EntityType union
            elif context.Type = typeof<EntityType> then
                schema.Type <- "string"

                schema.Enum <- [|
                    OpenApiString("User") :> IOpenApiAny
                    OpenApiString("App") :> IOpenApiAny
                    OpenApiString("Dashboard") :> IOpenApiAny
                    OpenApiString("Resource") :> IOpenApiAny
                    OpenApiString("Folder") :> IOpenApiAny
                    OpenApiString("Run") :> IOpenApiAny
                    OpenApiString("Space") :> IOpenApiAny
                |]

                schema.Properties <- null
                schema.AdditionalProperties <- null
                schema.Required <- null

type FSharpQueryParameterOperationFilter() =
    interface IOperationFilter with
        member _.Apply(operation: OpenApiOperation, context: OperationFilterContext) =
            if operation.Parameters <> null then
                for parameter in operation.Parameters do
                    // Fix F# option parameter names - remove .Value suffix
                    if parameter.Name.EndsWith(".Value") then
                        parameter.Name <- parameter.Name.Substring(0, parameter.Name.Length - 6)
                        // Convert to camelCase for consistency
                        if parameter.Name.Length > 0 then
                            parameter.Name <-
                                System.Char.ToLowerInvariant(parameter.Name.[0]).ToString()
                                + parameter.Name.Substring(1)