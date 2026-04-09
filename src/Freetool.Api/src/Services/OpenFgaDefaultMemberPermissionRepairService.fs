namespace Freetool.Api.Services

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects

type OpenFgaDefaultMemberPermissionRepairSpaceResult = {
    SpaceId: string
    SpaceName: string
    DesiredPermissions: string list
    CurrentPermissions: string list
    PermissionsToAdd: string list
    PermissionsToRemove: string list
    Applied: bool
    Warnings: string list
}

type OpenFgaDefaultMemberPermissionRepairSummary = {
    Apply: bool
    SpaceFilter: string option
    SpacesExamined: int
    SpacesWithDrift: int
    Results: OpenFgaDefaultMemberPermissionRepairSpaceResult list
}

type IOpenFgaDefaultMemberPermissionRepairService =
    abstract member RepairAsync:
        apply: bool -> requestedSpaceId: string option -> Task<OpenFgaDefaultMemberPermissionRepairSummary>

module OpenFgaDefaultMemberPermissionRepair =
    let allDefaultMemberPermissions = SpaceHandler.allSpacePermissions

    let private permissionNameToRelation =
        allDefaultMemberPermissions
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

type OpenFgaDefaultMemberPermissionRepairService
    (
        eventRepository: IEventRepository,
        spaceRepository: ISpaceRepository,
        authService: IAuthorizationService,
        logger: ILogger<OpenFgaDefaultMemberPermissionRepairService>
    ) =

    member private this.LoadAllDefaultMemberPermissionEventsAsync() : Task<Freetool.Domain.Entities.EventData list> =
        let defaultPermissionsChangedEventType: Freetool.Domain.Entities.SpaceEvents =
            Freetool.Domain.Entities.SpaceDefaultMemberPermissionsChangedEvent

        let rec loop skip acc = task {
            let! page =
                eventRepository.GetEventsAsync {
                    UserId = None
                    EventType = Some(Freetool.Domain.Entities.EventType.SpaceEvents defaultPermissionsChangedEventType)
                    EntityType = Some Freetool.Domain.Entities.EntityType.Space
                    FromDate = None
                    ToDate = None
                    Skip = skip
                    Take = 200
                }

            let nextAcc = page.Items @ acc

            if skip + page.Items.Length >= page.TotalCount || List.isEmpty page.Items then
                return nextAcc
            else
                return! loop (skip + page.Items.Length) nextAcc
        }

        loop 0 []

    member this.RepairAsync
        (apply: bool, requestedSpaceId: string option)
        : Task<OpenFgaDefaultMemberPermissionRepairSummary> =
        task {
            let! allEvents = this.LoadAllDefaultMemberPermissionEventsAsync()

            let filteredEvents =
                allEvents
                |> List.filter (fun eventData ->
                    requestedSpaceId
                    |> Option.forall (fun requestedId -> eventData.EntityId = requestedId))
                |> List.sortBy (fun eventData -> eventData.OccurredAt)

            let groupedEvents =
                filteredEvents
                |> List.groupBy (fun eventData -> eventData.EntityId)
                |> Map.ofList

            let! results =
                groupedEvents
                |> Map.toList
                |> List.map (fun (targetSpaceId, spaceEvents) -> task {
                    let mutable desiredPermissions = Set.empty<AuthRelation>
                    let mutable effectiveSpaceName = targetSpaceId
                    let mutable warnings: string list = []

                    for eventData in spaceEvents do
                        try
                            let parsedEvent =
                                OpenFgaDefaultMemberPermissionRepair.deserializeDefaultMemberPermissionEvent
                                    eventData.EventData

                            effectiveSpaceName <- parsedEvent.SpaceName

                            let granted =
                                parsedEvent.PermissionsGranted
                                |> List.choose (fun permissionName ->
                                    match OpenFgaDefaultMemberPermissionRepair.tryParseAuditName permissionName with
                                    | Some relation -> Some relation
                                    | None ->
                                        warnings <-
                                            $"Ignored unknown granted permission '{permissionName}' in event {eventData.EventId}"
                                            :: warnings

                                        None)
                                |> Set.ofList

                            let revoked =
                                parsedEvent.PermissionsRevoked
                                |> List.choose (fun permissionName ->
                                    match OpenFgaDefaultMemberPermissionRepair.tryParseAuditName permissionName with
                                    | Some relation -> Some relation
                                    | None ->
                                        warnings <-
                                            $"Ignored unknown revoked permission '{permissionName}' in event {eventData.EventId}"
                                            :: warnings

                                        None)
                                |> Set.ofList

                            desiredPermissions <- Set.union granted (Set.difference desiredPermissions revoked)
                        with ex ->
                            logger.LogWarning(
                                ex,
                                "Failed to deserialize default member permission event {EventId} for space {SpaceId}",
                                eventData.EventId,
                                targetSpaceId
                            )

                            warnings <-
                                $"Failed to deserialize default member permission event {eventData.EventId}: {ex.Message}"
                                :: warnings

                    let! persistedSpaceName = task {
                        match Guid.TryParse targetSpaceId with
                        | true, guid ->
                            let! spaceOption = spaceRepository.GetByIdAsync(SpaceId.FromGuid guid)

                            return
                                match spaceOption with
                                | Some space -> Some space.State.Name
                                | None -> None
                        | false, _ -> return None
                    }

                    let spaceName = persistedSpaceName |> Option.defaultValue effectiveSpaceName
                    let defaultMemberSubject = UserSetFromRelation("space", targetSpaceId, "member")

                    let! currentPermissionsMap =
                        authService.BatchCheckPermissionsAsync
                            defaultMemberSubject
                            OpenFgaDefaultMemberPermissionRepair.allDefaultMemberPermissions
                            (SpaceObject targetSpaceId)

                    let currentPermissions =
                        currentPermissionsMap
                        |> Map.toList
                        |> List.choose (fun (relation, isAllowed) -> if isAllowed then Some relation else None)
                        |> Set.ofList

                    let permissionsToAdd =
                        Set.difference desiredPermissions currentPermissions |> Set.toList

                    let permissionsToRemove =
                        Set.difference currentPermissions desiredPermissions |> Set.toList

                    if
                        apply
                        && (not (List.isEmpty permissionsToAdd) || not (List.isEmpty permissionsToRemove))
                    then
                        do!
                            authService.UpdateRelationshipsAsync {
                                TuplesToAdd =
                                    permissionsToAdd
                                    |> List.map (fun relation -> {
                                        Subject = defaultMemberSubject
                                        Relation = relation
                                        Object = SpaceObject targetSpaceId
                                    })
                                TuplesToRemove =
                                    permissionsToRemove
                                    |> List.map (fun relation -> {
                                        Subject = defaultMemberSubject
                                        Relation = relation
                                        Object = SpaceObject targetSpaceId
                                    })
                            }

                    return {
                        SpaceId = targetSpaceId
                        SpaceName = spaceName
                        DesiredPermissions =
                            desiredPermissions
                            |> Set.toList
                            |> List.map OpenFgaDefaultMemberPermissionRepair.relationToAuditName
                            |> List.sort
                        CurrentPermissions =
                            currentPermissions
                            |> Set.toList
                            |> List.map OpenFgaDefaultMemberPermissionRepair.relationToAuditName
                            |> List.sort
                        PermissionsToAdd =
                            permissionsToAdd
                            |> List.map OpenFgaDefaultMemberPermissionRepair.relationToAuditName
                            |> List.sort
                        PermissionsToRemove =
                            permissionsToRemove
                            |> List.map OpenFgaDefaultMemberPermissionRepair.relationToAuditName
                            |> List.sort
                        Applied =
                            apply
                            && (not (List.isEmpty permissionsToAdd) || not (List.isEmpty permissionsToRemove))
                        Warnings = warnings |> List.rev
                    }
                })
                |> Task.WhenAll

            let materializedResults = results |> Array.toList

            let spacesWithDrift =
                materializedResults
                |> List.sumBy (fun result ->
                    if
                        not (List.isEmpty result.PermissionsToAdd)
                        || not (List.isEmpty result.PermissionsToRemove)
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

    interface IOpenFgaDefaultMemberPermissionRepairService with
        member this.RepairAsync apply requestedSpaceId = this.RepairAsync(apply, requestedSpaceId)
