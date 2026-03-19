namespace Freetool.Application.Services

open System
open System.Threading.Tasks
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events
open Freetool.Application.Interfaces
open Freetool.Application.DTOs

type IEventEnhancementService =
    abstract member EnhanceEventAsync: EventData -> Task<EnhancedEventData>

type EventEnhancementService
    (
        userRepository: IUserRepository,
        appRepository: IAppRepository,
        dashboardRepository: IDashboardRepository,
        folderRepository: IFolderRepository,
        resourceRepository: IResourceRepository,
        spaceRepository: ISpaceRepository
    ) =

    // Shared JSON options for deserializing F# event types
    let jsonOptions =
        let options = JsonSerializerOptions()
        options.Converters.Add(JsonFSharpConverter())
        options

    let redactAuthorizationHeadersInEventData (eventData: string) : string =
        let tryGetString (node: JsonNode) =
            match node with
            | :? JsonValue as value ->
                let mutable result = Unchecked.defaultof<string>
                if value.TryGetValue(&result) then Some result else None
            | _ -> None

        let tryRedactHeaderObject (obj: JsonObject) =
            match obj["Key"], obj["Value"] with
            | null, _
            | _, null -> ()
            | keyNode, valueNode ->
                match tryGetString keyNode, tryGetString valueNode with
                | Some key, Some value when AuthorizationHeaderRedaction.isAuthorizationHeaderKey key ->
                    let redactedValue =
                        AuthorizationHeaderRedaction.redactAuthorizationHeaderValue value

                    obj["Value"] <- JsonValue.Create(redactedValue)
                | _ -> ()

        let rec walk (node: JsonNode) =
            match node with
            | :? JsonObject as obj ->
                tryRedactHeaderObject obj

                for property in obj do
                    if not (isNull property.Value) then
                        walk property.Value
            | :? JsonArray as array ->
                for item in array do
                    if not (isNull item) then
                        walk item
            | _ -> ()

        try
            let root = JsonNode.Parse(eventData)

            if isNull root then
                eventData
            else
                walk root
                root.ToJsonString()
        with _ ->
            eventData

    let extractEntityNameFromEventDataAsync
        (eventData: string)
        (eventType: EventType)
        (entityType: EntityType)
        : Task<string> =
        task {
            try
                match eventType with
                | UserEvents userEvent ->
                    match userEvent with
                    | UserCreatedEvent ->
                        let userEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.UserCreatedEvent>(eventData, jsonOptions)

                        return userEventData.Name
                    | UserInvitedEvent ->
                        let userEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.UserInvitedEvent>(eventData, jsonOptions)

                        // Use email as entity name since invited users don't have a name yet
                        return userEventData.Email.Value
                    | UserActivatedEvent ->
                        let userEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.UserActivatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return userEventData.Name
                    | UserDeletedEvent ->
                        let userEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.UserDeletedEvent>(eventData, jsonOptions)

                        return userEventData.Name
                    | UserUpdatedEvent ->
                        // For update events, look up current name from repository
                        let userEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.UserUpdatedEvent>(eventData, jsonOptions)

                        let! user = userRepository.GetByIdAsync userEventData.UserId

                        match user with
                        | Some u -> return User.getName u
                        | None -> return $"User {userEventData.UserId.Value}"
                | AppEvents appEvent ->
                    match appEvent with
                    | AppCreatedEvent ->
                        let appEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.AppCreatedEvent>(eventData, jsonOptions)

                        return appEventData.Name.Value
                    | AppUpdatedEvent ->
                        let appEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.AppUpdatedEvent>(eventData, jsonOptions)

                        let! app = appRepository.GetByIdAsync appEventData.AppId

                        match app with
                        | Some a -> return App.getName a
                        | None -> return $"App {appEventData.AppId.Value}"
                    | AppDeletedEvent ->
                        let appEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.AppDeletedEvent>(eventData, jsonOptions)

                        return appEventData.Name.Value
                    | AppRestoredEvent ->
                        let appEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.AppRestoredEvent>(eventData, jsonOptions)

                        return appEventData.Name.Value
                | DashboardEvents dashboardEvent ->
                    match dashboardEvent with
                    | DashboardCreatedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardCreatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return dashboardEventData.Name.Value
                    | DashboardUpdatedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardUpdatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! dashboard = dashboardRepository.GetByIdAsync dashboardEventData.DashboardId

                        match dashboard with
                        | Some d -> return Dashboard.getName d
                        | None -> return $"Dashboard {dashboardEventData.DashboardId.Value}"
                    | DashboardDeletedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardDeletedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return dashboardEventData.Name.Value
                    | DashboardPreparedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardPreparedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! dashboard = dashboardRepository.GetByIdAsync dashboardEventData.DashboardId

                        match dashboard with
                        | Some d -> return Dashboard.getName d
                        | None -> return $"Dashboard {dashboardEventData.DashboardId.Value}"
                    | DashboardPrepareFailedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardPrepareFailedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! dashboard = dashboardRepository.GetByIdAsync dashboardEventData.DashboardId

                        match dashboard with
                        | Some d -> return Dashboard.getName d
                        | None -> return $"Dashboard {dashboardEventData.DashboardId.Value}"
                    | DashboardActionExecutedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardActionExecutedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! dashboard = dashboardRepository.GetByIdAsync dashboardEventData.DashboardId

                        match dashboard with
                        | Some d -> return Dashboard.getName d
                        | None -> return $"Dashboard {dashboardEventData.DashboardId.Value}"
                    | DashboardActionFailedEvent ->
                        let dashboardEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.DashboardActionFailedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! dashboard = dashboardRepository.GetByIdAsync dashboardEventData.DashboardId

                        match dashboard with
                        | Some d -> return Dashboard.getName d
                        | None -> return $"Dashboard {dashboardEventData.DashboardId.Value}"
                | FolderEvents folderEvent ->
                    match folderEvent with
                    | FolderCreatedEvent ->
                        let folderEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.FolderCreatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return folderEventData.Name.Value
                    | FolderUpdatedEvent ->
                        let folderEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.FolderUpdatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! folder = folderRepository.GetByIdAsync folderEventData.FolderId

                        match folder with
                        | Some f -> return Folder.getName f
                        | None -> return $"Folder {folderEventData.FolderId.Value}"
                    | FolderDeletedEvent ->
                        let folderEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.FolderDeletedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return folderEventData.Name.Value
                    | FolderRestoredEvent ->
                        let folderEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.FolderRestoredEvent>(
                                eventData,
                                jsonOptions
                            )

                        return folderEventData.Name.Value
                | ResourceEvents resourceEvent ->
                    match resourceEvent with
                    | ResourceCreatedEvent ->
                        let resourceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.ResourceCreatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return resourceEventData.Name.Value
                    | ResourceUpdatedEvent ->
                        let resourceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.ResourceUpdatedEvent>(
                                eventData,
                                jsonOptions
                            )

                        let! resource = resourceRepository.GetByIdAsync resourceEventData.ResourceId

                        match resource with
                        | Some r -> return Resource.getName r
                        | None -> return $"Resource {resourceEventData.ResourceId.Value}"
                    | ResourceDeletedEvent ->
                        let resourceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.ResourceDeletedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return resourceEventData.Name.Value
                    | ResourceRestoredEvent ->
                        let resourceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.ResourceRestoredEvent>(
                                eventData,
                                jsonOptions
                            )

                        return resourceEventData.Name.Value
                | RunEvents runEvent ->
                    match runEvent with
                    | RunCreatedEvent ->
                        let runEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.RunCreatedEvent>(eventData, jsonOptions)

                        // Check if AppId is empty/null GUID
                        if runEventData.AppId.Value = Guid.Empty then
                            return "Run (Empty App ID)"
                        else
                            let! app = appRepository.GetByIdAsync runEventData.AppId

                            match app with
                            | Some appData -> return App.getName appData
                            | None -> return $"Run (App {runEventData.AppId.Value} not found)"
                    | RunStatusChangedEvent ->
                        // For status changes, we don't have the AppId directly, so fallback to Run ID
                        let runEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.RunStatusChangedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return $"Run {runEventData.RunId.Value}"
                | SpaceEvents spaceEvent ->
                    match spaceEvent with
                    | SpaceCreatedEvent ->
                        let spaceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.SpaceCreatedEvent>(eventData, jsonOptions)

                        return spaceEventData.Name
                    | SpaceUpdatedEvent ->
                        // SpaceUpdatedEvent doesn't have Name directly, look up from repository
                        let spaceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.SpaceUpdatedEvent>(eventData, jsonOptions)

                        let! space = spaceRepository.GetByIdAsync spaceEventData.SpaceId

                        match space with
                        | Some s -> return Space.getName s
                        | None -> return $"Space {spaceEventData.SpaceId.Value}"
                    | SpaceDeletedEvent ->
                        // SpaceDeletedEvent has Name
                        let spaceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.SpaceDeletedEvent>(eventData, jsonOptions)

                        return spaceEventData.Name
                    | SpacePermissionsChangedEvent ->
                        // SpacePermissionsChangedEvent has SpaceName
                        let spaceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.SpacePermissionsChangedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return spaceEventData.SpaceName
                    | SpaceDefaultMemberPermissionsChangedEvent ->
                        // SpaceDefaultMemberPermissionsChangedEvent has SpaceName
                        let spaceEventData =
                            JsonSerializer.Deserialize<Freetool.Domain.Events.SpaceDefaultMemberPermissionsChangedEvent>(
                                eventData,
                                jsonOptions
                            )

                        return spaceEventData.SpaceName
            with ex ->
                let entityTypeStr = EntityTypeConverter.toString entityType
                // Include some of the raw event data for debugging
                let dataPreview =
                    if eventData.Length > 100 then
                        eventData.Substring(0, 100) + "..."
                    else
                        eventData

                return $"{entityTypeStr} (Parse Error: {ex.Message} | Data: {dataPreview})"
        }

    let getUserNameAsync (userId: UserId) : Task<string> = task {
        try
            let! user = userRepository.GetByIdAsync userId

            match user with
            | Some u -> return User.getName u
            | None -> return $"User {userId.Value}"
        with _ ->
            return $"User {userId.Value}"
    }

    let generateEventSummary (eventType: EventType) (entityName: string) (userName: string) : string =
        match eventType with
        | UserEvents userEvent ->
            match userEvent with
            | UserCreatedEvent -> $"{userName} created user \"{entityName}\""
            | UserUpdatedEvent -> $"{userName} updated user \"{entityName}\""
            | UserDeletedEvent -> $"{userName} deleted user \"{entityName}\""
            | UserInvitedEvent -> $"{userName} invited user \"{entityName}\""
            | UserActivatedEvent -> $"User \"{entityName}\" activated their account"
        | AppEvents appEvent ->
            match appEvent with
            | AppCreatedEvent -> $"{userName} created app \"{entityName}\""
            | AppUpdatedEvent -> $"{userName} updated app \"{entityName}\""
            | AppDeletedEvent -> $"{userName} deleted app \"{entityName}\""
            | AppRestoredEvent -> $"{userName} restored app \"{entityName}\""
        | DashboardEvents dashboardEvent ->
            match dashboardEvent with
            | DashboardCreatedEvent -> $"{userName} created dashboard \"{entityName}\""
            | DashboardUpdatedEvent -> $"{userName} updated dashboard \"{entityName}\""
            | DashboardDeletedEvent -> $"{userName} deleted dashboard \"{entityName}\""
            | DashboardPreparedEvent -> $"{userName} prepared dashboard \"{entityName}\""
            | DashboardPrepareFailedEvent -> $"{userName} failed to prepare dashboard \"{entityName}\""
            | DashboardActionExecutedEvent -> $"{userName} executed a dashboard action in \"{entityName}\""
            | DashboardActionFailedEvent -> $"{userName} failed a dashboard action in \"{entityName}\""
        | FolderEvents folderEvent ->
            match folderEvent with
            | FolderCreatedEvent -> $"{userName} created folder \"{entityName}\""
            | FolderUpdatedEvent -> $"{userName} updated folder \"{entityName}\""
            | FolderDeletedEvent -> $"{userName} deleted folder \"{entityName}\""
            | FolderRestoredEvent -> $"{userName} restored folder \"{entityName}\""
        | ResourceEvents resourceEvent ->
            match resourceEvent with
            | ResourceCreatedEvent -> $"{userName} created resource \"{entityName}\""
            | ResourceUpdatedEvent -> $"{userName} updated resource \"{entityName}\""
            | ResourceDeletedEvent -> $"{userName} deleted resource \"{entityName}\""
            | ResourceRestoredEvent -> $"{userName} restored resource \"{entityName}\""
        | RunEvents runEvent ->
            match runEvent with
            | RunCreatedEvent -> $"{userName} ran \"{entityName}\""
            | RunStatusChangedEvent -> $"{userName} changed status of \"{entityName}\""
        | SpaceEvents spaceEvent ->
            match spaceEvent with
            | SpaceCreatedEvent -> $"{userName} created space \"{entityName}\""
            | SpaceUpdatedEvent -> $"{userName} updated space \"{entityName}\""
            | SpaceDeletedEvent -> $"{userName} deleted space \"{entityName}\""
            | SpacePermissionsChangedEvent -> $"{userName} changed member permissions in space \"{entityName}\""
            | SpaceDefaultMemberPermissionsChangedEvent ->
                $"{userName} changed default member permissions in space \"{entityName}\""

    interface IEventEnhancementService with
        member this.EnhanceEventAsync(event: EventData) : Task<EnhancedEventData> = task {
            let! userName = getUserNameAsync event.UserId

            let! entityName = extractEntityNameFromEventDataAsync event.EventData event.EventType event.EntityType

            let eventSummary = generateEventSummary event.EventType entityName userName

            let redactedEventData = redactAuthorizationHeadersInEventData event.EventData

            return {
                Id = event.Id
                EventId = event.EventId
                EventType = event.EventType
                EntityType = event.EntityType
                EntityId = event.EntityId
                EntityName = entityName
                EventData = redactedEventData
                OccurredAt = event.OccurredAt
                CreatedAt = event.CreatedAt
                UserId = event.UserId
                UserName = userName
                EventSummary = eventSummary
            }
        }