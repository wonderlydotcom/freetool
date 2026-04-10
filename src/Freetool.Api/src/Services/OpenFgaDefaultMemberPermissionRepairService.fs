namespace Freetool.Api.Services

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects

type OpenFgaSpaceAuthorizationRepairSpaceResult = {
    SpaceId: string
    SpaceName: string
    DesiredRelationships: string list
    CurrentRelationships: string list
    RelationshipsToAdd: string list
    RelationshipsToRemove: string list
    Applied: bool
    Warnings: string list
}

type OpenFgaSpaceAuthorizationRepairSummary = {
    Apply: bool
    SpaceFilter: string option
    SpacesExamined: int
    SpacesWithDrift: int
    Results: OpenFgaSpaceAuthorizationRepairSpaceResult list
}

type IOpenFgaSpaceAuthorizationRepairService =
    abstract member RepairAsync:
        apply: bool -> requestedSpaceId: string option -> Task<OpenFgaSpaceAuthorizationRepairSummary>

module OpenFgaSpaceAuthorizationRepairStartup =
    let runOpenFgaSpaceAuthorizationRepair
        (logger: ILogger)
        (repair: unit -> OpenFgaSpaceAuthorizationRepairSummary)
        : unit =
        try
            logger.LogInformation("Repairing OpenFGA space authorization tuples from persisted space state...")

            let repairSummary = repair ()

            let warningCount =
                repairSummary.Results |> List.sumBy (fun result -> List.length result.Warnings)

            logger.LogInformation(
                "OpenFGA space authorization repair examined {SpacesExamined} spaces and repaired {SpacesWithDrift} spaces",
                repairSummary.SpacesExamined,
                repairSummary.SpacesWithDrift
            )

            if warningCount > 0 then
                logger.LogWarning(
                    "OpenFGA space authorization repair completed with {WarningCount} warnings",
                    warningCount
                )
        with ex ->
            logger.LogWarning("Could not repair OpenFGA space authorization tuples: {Error}", ex.Message)

module OpenFgaSpaceAuthorizationRepair =
    let allManagedPermissions = SpaceHandler.allSpacePermissions

    // RunApp predates the current audit-backed default-permission repair flow and is expected on all spaces.
    // Keep it as a reconciliation baseline until default-member permissions become durable stored state.
    let baselineDefaultMemberPermissions = Set.ofList [ AppRun ]

    let private permissionNameToRelation =
        allManagedPermissions
        |> List.map (fun relation -> (SpaceHandler.authRelationToString relation, relation))
        |> Map.ofList

    let relationToAuditName (relation: AuthRelation) =
        SpaceHandler.authRelationToString relation

    let tryParseAuditName (permissionName: string) =
        permissionNameToRelation |> Map.tryFind permissionName

    let private serializerOptions =
        let options = JsonSerializerOptions()
        options.Converters.Add(JsonFSharpConverter())
        options

    let deserializeDefaultMemberPermissionEvent (rawEventData: string) =
        JsonSerializer.Deserialize<SpaceDefaultMemberPermissionsChangedEvent>(rawEventData, serializerOptions)

    let deserializeUserPermissionEvent (rawEventData: string) =
        JsonSerializer.Deserialize<SpacePermissionsChangedEvent>(rawEventData, serializerOptions)

    let relationshipToDisplay (tuple: RelationshipTuple) = RelationshipTuple.toDisplayString tuple

type OpenFgaSpaceAuthorizationRepairService
    (
        eventRepository: IEventRepository,
        spaceRepository: ISpaceRepository,
        authService: IAuthorizationService,
        relationshipReader: IAuthorizationRelationshipReader,
        logger: ILogger<OpenFgaSpaceAuthorizationRepairService>
    ) =

    member private _.LoadAllSpacesAsync() : Task<ValidatedSpace list> =
        let rec loop skip acc = task {
            let! page = spaceRepository.GetAllAsync skip 100
            let nextAcc = acc @ page

            if List.length page < 100 then
                return nextAcc
            else
                return! loop (skip + 100) nextAcc
        }

        loop 0 []

    member private _.LoadAllSpaceEventsAsync
        (spaceEventType: Freetool.Domain.Entities.SpaceEvents)
        : Task<EventData list> =
        let rec loop skip acc = task {
            let! page =
                eventRepository.GetEventsAsync {
                    UserId = None
                    EventType = Some(EventType.SpaceEvents spaceEventType)
                    EntityType = Some EntityType.Space
                    FromDate = None
                    ToDate = None
                    Skip = skip
                    Take = 100
                }

            let nextAcc = acc @ page.Items

            if List.length page.Items < page.Take then
                return nextAcc
            else
                return! loop (skip + page.Take) nextAcc
        }

        loop 0 []

    member private _.ReplayDefaultMemberPermissions(spaceId: string, events: EventData list) =
        let mutable desiredPermissions =
            OpenFgaSpaceAuthorizationRepair.baselineDefaultMemberPermissions

        let mutable warnings: string list = []

        let parsePermissions kind permissionNames eventId =
            permissionNames
            |> List.choose (fun permissionName ->
                match OpenFgaSpaceAuthorizationRepair.tryParseAuditName permissionName with
                | Some relation -> Some relation
                | None ->
                    warnings <-
                        $"Ignored unknown {kind} permission '{permissionName}' in event {eventId}"
                        :: warnings

                    None)
            |> Set.ofList

        for eventData in events |> List.sortBy (fun item -> item.OccurredAt) do
            try
                let parsedEvent =
                    OpenFgaSpaceAuthorizationRepair.deserializeDefaultMemberPermissionEvent eventData.EventData

                let granted =
                    parsePermissions "granted default-member" parsedEvent.PermissionsGranted eventData.EventId

                let revoked =
                    parsePermissions "revoked default-member" parsedEvent.PermissionsRevoked eventData.EventId

                desiredPermissions <- Set.union granted (Set.difference desiredPermissions revoked)
            with ex ->
                logger.LogWarning(
                    ex,
                    "Failed to deserialize default member permission event {EventId} for space {SpaceId}",
                    eventData.EventId,
                    spaceId
                )

                warnings <-
                    $"Failed to deserialize default member permission event {eventData.EventId}: {ex.Message}"
                    :: warnings

        (desiredPermissions, warnings |> List.rev)

    member private _.ReplayExplicitUserPermissions(space: ValidatedSpace, events: EventData list) =
        let currentMemberIds =
            space.State.MemberIds
            |> List.filter (fun memberId -> memberId <> space.State.ModeratorUserId)
            |> List.map (fun memberId -> memberId.Value.ToString())
            |> Set.ofList

        let mutable desiredPermissionsByUser: Map<string, Set<AuthRelation>> = Map.empty
        let mutable warnings: string list = []

        let parsePermissions kind permissionNames eventId =
            permissionNames
            |> List.choose (fun permissionName ->
                match OpenFgaSpaceAuthorizationRepair.tryParseAuditName permissionName with
                | Some relation -> Some relation
                | None ->
                    warnings <-
                        $"Ignored unknown {kind} permission '{permissionName}' in event {eventId}"
                        :: warnings

                    None)
            |> Set.ofList

        for eventData in events |> List.sortBy (fun item -> item.OccurredAt) do
            try
                let parsedEvent =
                    OpenFgaSpaceAuthorizationRepair.deserializeUserPermissionEvent eventData.EventData

                let targetUserId = parsedEvent.TargetUserId.Value.ToString()

                let granted =
                    parsePermissions "granted member" parsedEvent.PermissionsGranted eventData.EventId

                let revoked =
                    parsePermissions "revoked member" parsedEvent.PermissionsRevoked eventData.EventId

                let currentPermissions =
                    desiredPermissionsByUser
                    |> Map.tryFind targetUserId
                    |> Option.defaultValue Set.empty

                let updatedPermissions =
                    Set.union granted (Set.difference currentPermissions revoked)

                desiredPermissionsByUser <-
                    if Set.isEmpty updatedPermissions then
                        desiredPermissionsByUser |> Map.remove targetUserId
                    else
                        desiredPermissionsByUser |> Map.add targetUserId updatedPermissions
            with ex ->
                logger.LogWarning(
                    ex,
                    "Failed to deserialize member permission event {EventId} for space {SpaceId}",
                    eventData.EventId,
                    space.State.Id.Value
                )

                warnings <-
                    $"Failed to deserialize member permission event {eventData.EventId}: {ex.Message}"
                    :: warnings

        let filteredPermissionsByUser =
            desiredPermissionsByUser
            |> Map.filter (fun userId _ -> Set.contains userId currentMemberIds)

        (filteredPermissionsByUser, warnings |> List.rev)

    member private this.BuildDesiredRelationships
        (space: ValidatedSpace, defaultMemberEvents: EventData list, userEvents: EventData list)
        : RelationshipTuple list * string list =
        let spaceId = space.State.Id.Value.ToString()
        let defaultMemberSubject = UserSetFromRelation("space", spaceId, "member")

        let defaultMemberPermissions, defaultWarnings =
            this.ReplayDefaultMemberPermissions(spaceId, defaultMemberEvents)

        let explicitUserPermissions, explicitWarnings =
            this.ReplayExplicitUserPermissions(space, userEvents)

        let memberRelationships =
            space.State.MemberIds
            |> List.distinct
            |> List.map (fun memberId -> {
                Subject = Freetool.Application.Interfaces.User(memberId.Value.ToString())
                Relation = SpaceMember
                Object = SpaceObject spaceId
            })

        let defaultMemberPermissionRelationships =
            defaultMemberPermissions
            |> Set.toList
            |> List.map (fun relation -> {
                Subject = defaultMemberSubject
                Relation = relation
                Object = SpaceObject spaceId
            })

        let explicitPermissionRelationships =
            explicitUserPermissions
            |> Map.toList
            |> List.collect (fun (userId, permissions) ->
                permissions
                |> Set.toList
                |> List.map (fun relation -> {
                    Subject = Freetool.Application.Interfaces.User userId
                    Relation = relation
                    Object = SpaceObject spaceId
                }))

        let desiredRelationships =
            [
                {
                    Subject = Freetool.Application.Interfaces.Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject spaceId
                }
                {
                    Subject = Freetool.Application.Interfaces.User(space.State.ModeratorUserId.Value.ToString())
                    Relation = SpaceModerator
                    Object = SpaceObject spaceId
                }
            ]
            @ memberRelationships
            @ defaultMemberPermissionRelationships
            @ explicitPermissionRelationships
            |> List.distinctBy RelationshipTuple.toDisplayString

        (desiredRelationships, defaultWarnings @ explicitWarnings)

    member this.RepairAsync
        (apply: bool, requestedSpaceId: string option)
        : Task<OpenFgaSpaceAuthorizationRepairSummary> =
        task {
            let! spaces = this.LoadAllSpacesAsync()

            let targetSpaces =
                match requestedSpaceId with
                | Some spaceId -> spaces |> List.filter (fun space -> space.State.Id.Value.ToString() = spaceId)
                | None -> spaces

            let! defaultMemberEvents =
                this.LoadAllSpaceEventsAsync Freetool.Domain.Entities.SpaceDefaultMemberPermissionsChangedEvent

            let! userPermissionEvents =
                this.LoadAllSpaceEventsAsync Freetool.Domain.Entities.SpacePermissionsChangedEvent

            let defaultMemberEventsBySpace =
                defaultMemberEvents
                |> List.groupBy (fun eventData -> eventData.EntityId)
                |> Map.ofList

            let userPermissionEventsBySpace =
                userPermissionEvents
                |> List.groupBy (fun eventData -> eventData.EntityId)
                |> Map.ofList

            let! results =
                targetSpaces
                |> List.map (fun space -> task {
                    let spaceId = space.State.Id.Value.ToString()

                    let defaultEvents =
                        defaultMemberEventsBySpace |> Map.tryFind spaceId |> Option.defaultValue []

                    let userEvents =
                        userPermissionEventsBySpace |> Map.tryFind spaceId |> Option.defaultValue []

                    let desiredRelationships, warnings =
                        this.BuildDesiredRelationships(space, defaultEvents, userEvents)

                    let! currentRelationships = relationshipReader.ReadRelationshipsAsync(SpaceObject spaceId)

                    let desiredLookup =
                        desiredRelationships
                        |> List.distinctBy RelationshipTuple.toDisplayString
                        |> List.map (fun tuple -> (RelationshipTuple.toDisplayString tuple, tuple))
                        |> Map.ofList

                    let currentLookup =
                        currentRelationships
                        |> List.distinctBy RelationshipTuple.toDisplayString
                        |> List.map (fun tuple -> (RelationshipTuple.toDisplayString tuple, tuple))
                        |> Map.ofList

                    let relationshipsToAdd =
                        desiredLookup
                        |> Map.toList
                        |> List.choose (fun (key, tuple) ->
                            if currentLookup |> Map.containsKey key then
                                None
                            else
                                Some tuple)

                    let relationshipsToRemove =
                        currentLookup
                        |> Map.toList
                        |> List.choose (fun (key, tuple) ->
                            if desiredLookup |> Map.containsKey key then
                                None
                            else
                                Some tuple)

                    if
                        apply
                        && (not (List.isEmpty relationshipsToAdd)
                            || not (List.isEmpty relationshipsToRemove))
                    then
                        do!
                            authService.UpdateRelationshipsAsync {
                                TuplesToAdd = relationshipsToAdd
                                TuplesToRemove = relationshipsToRemove
                            }

                    return {
                        SpaceId = spaceId
                        SpaceName = space.State.Name
                        DesiredRelationships =
                            desiredRelationships
                            |> List.map OpenFgaSpaceAuthorizationRepair.relationshipToDisplay
                            |> List.sort
                        CurrentRelationships =
                            currentRelationships
                            |> List.map OpenFgaSpaceAuthorizationRepair.relationshipToDisplay
                            |> List.sort
                        RelationshipsToAdd =
                            relationshipsToAdd
                            |> List.map OpenFgaSpaceAuthorizationRepair.relationshipToDisplay
                            |> List.sort
                        RelationshipsToRemove =
                            relationshipsToRemove
                            |> List.map OpenFgaSpaceAuthorizationRepair.relationshipToDisplay
                            |> List.sort
                        Applied =
                            apply
                            && (not (List.isEmpty relationshipsToAdd)
                                || not (List.isEmpty relationshipsToRemove))
                        Warnings = warnings
                    }
                })
                |> Task.WhenAll

            let materializedResults = results |> Array.toList

            let spacesWithDrift =
                materializedResults
                |> List.sumBy (fun result ->
                    if
                        not (List.isEmpty result.RelationshipsToAdd)
                        || not (List.isEmpty result.RelationshipsToRemove)
                    then
                        1
                    else
                        0)

            return {
                Apply = apply
                SpaceFilter = requestedSpaceId
                SpacesExamined = materializedResults.Length
                SpacesWithDrift = spacesWithDrift
                Results = materializedResults
            }
        }

    interface IOpenFgaSpaceAuthorizationRepairService with
        member this.RepairAsync apply requestedSpaceId =
            this.RepairAsync(apply, requestedSpaceId)