module Freetool.Infrastructure.Tests.OpenFgaSpaceAuthorizationRepairServiceTests

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Xunit
open Microsoft.Extensions.Logging.Abstractions
open Freetool.Api.Services
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects

let private serializerOptions =
    let options = JsonSerializerOptions()
    options.Converters.Add(JsonFSharpConverter())
    options

type MockEventRepository(events: EventData list) =
    interface IEventRepository with
        member _.SaveEventAsync(_event: IDomainEvent) = Task.FromResult(())
        member _.CommitAsync() = Task.FromResult(())

        member _.GetEventsAsync(filter: EventFilter) : Task<PagedResult<EventData>> = task {
            let filtered =
                events
                |> List.filter (fun event ->
                    filter.EventType |> Option.forall (fun eventType -> event.EventType = eventType))
                |> List.filter (fun event ->
                    filter.EntityType
                    |> Option.forall (fun entityType -> event.EntityType = entityType))
                |> List.sortBy (fun event -> event.OccurredAt)

            let items = filtered |> List.skip filter.Skip |> List.truncate filter.Take

            return {
                Items = items
                TotalCount = filtered.Length
                Skip = filter.Skip
                Take = filter.Take
            }
        }

        member _.GetEventsByAppIdAsync(_filter: AppEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

        member _.GetEventsByDashboardIdAsync(_filter: DashboardEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

        member _.GetEventsByUserIdAsync(_filter: UserEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

type MockSpaceRepository(spaces: ValidatedSpace list) =
    interface ISpaceRepository with
        member _.GetByIdAsync(spaceId: SpaceId) : Task<ValidatedSpace option> = task {
            return spaces |> List.tryFind (fun space -> space.State.Id = spaceId)
        }

        member _.GetByNameAsync(name: string) : Task<ValidatedSpace option> = task {
            return spaces |> List.tryFind (fun space -> space.State.Name = name)
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedSpace list> = task {
            return spaces |> List.skip skip |> List.truncate take
        }

        member _.GetByUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaces |> List.filter (fun space -> List.contains userId space.State.MemberIds)
        }

        member _.GetByModeratorUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaces |> List.filter (fun space -> space.State.ModeratorUserId = userId)
        }

        member _.AddAsync(_space: ValidatedSpace) = Task.FromResult(Ok())
        member _.UpdateAsync(_space: ValidatedSpace) = Task.FromResult(Ok())
        member _.DeleteAsync(_space: ValidatedSpace) = Task.FromResult(Ok())

        member _.ExistsAsync(spaceId: SpaceId) =
            Task.FromResult(spaces |> List.exists (fun space -> space.State.Id = spaceId))

        member _.ExistsByNameAsync(name: string) =
            Task.FromResult(spaces |> List.exists (fun space -> space.State.Name = name))

        member _.GetCountAsync() = Task.FromResult(spaces.Length)

type TrackingAuthorizationService() =
    let mutable tuples: Map<string, RelationshipTuple> = Map.empty
    let mutable updateRequests: UpdateRelationshipsRequest list = []

    let key (tuple: RelationshipTuple) = RelationshipTuple.toDisplayString tuple

    member _.UpdateRequests = updateRequests

    member _.SeedTuple(tuple: RelationshipTuple) =
        tuples <- tuples |> Map.add (key tuple) tuple

    interface IAuthorizationService with
        member _.CreateStoreAsync request =
            Task.FromResult(
                {
                    Id = Guid.NewGuid().ToString()
                    Name = request.Name
                }
            )

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult(
                {
                    AuthorizationModelId = Guid.NewGuid().ToString()
                }
            )

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())

        member _.CreateRelationshipsAsync(newTuples: RelationshipTuple list) = task {
            for tuple in newTuples do
                tuples <- tuples |> Map.add (key tuple) tuple
        }

        member _.UpdateRelationshipsAsync(request: UpdateRelationshipsRequest) = task {
            updateRequests <- updateRequests @ [ request ]

            for tuple in request.TuplesToAdd do
                tuples <- tuples |> Map.add (key tuple) tuple

            for tuple in request.TuplesToRemove do
                tuples <- tuples |> Map.remove (key tuple)
        }

        member _.DeleteRelationshipsAsync(removedTuples: RelationshipTuple list) = task {
            for tuple in removedTuples do
                tuples <- tuples |> Map.remove (key tuple)
        }

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
            Task.FromResult(
                tuples
                |> Map.containsKey (
                    RelationshipTuple.toDisplayString {
                        Subject = subject
                        Relation = relation
                        Object = obj
                    }
                )
            )

        member this.BatchCheckPermissionsAsync
            (subject: AuthSubject)
            (relations: AuthRelation list)
            (obj: AuthObject)
            : Task<Map<AuthRelation, bool>> =
            task {
                let! results =
                    relations
                    |> List.map (fun relation -> task {
                        let! allowed = (this :> IAuthorizationService).CheckPermissionAsync subject relation obj
                        return (relation, allowed)
                    })
                    |> Task.WhenAll

                return results |> Array.toList |> Map.ofList
            }

        member _.StoreExistsAsync _ = Task.FromResult(true)

    interface IAuthorizationRelationshipReader with
        member _.ReadRelationshipsAsync(obj: AuthObject) =
            Task.FromResult(
                tuples
                |> Map.values
                |> Seq.filter (fun tuple -> tuple.Object = obj)
                |> Seq.toList
            )

let private createSpace (name: string) (memberIds: UserId list) =
    let actorUserId = UserId.NewId()
    let moderatorUserId = UserId.NewId()

    match Space.create actorUserId name moderatorUserId (Some memberIds) with
    | Ok space -> space
    | Error error -> failwithf "Failed to create test space: %A" error

let private createDefaultPermissionEvent
    (actorUserId: UserId)
    (space: ValidatedSpace)
    (granted: string list)
    (revoked: string list)
    (occurredAt: DateTime)
    =
    let event =
        SpaceEvents.spaceDefaultMemberPermissionsChanged actorUserId space.State.Id space.State.Name granted revoked

    {
        Id = Guid.NewGuid()
        EventId = event.EventId.ToString()
        EventType = EventType.SpaceEvents Freetool.Domain.Entities.SpaceDefaultMemberPermissionsChangedEvent
        EntityType = EntityType.Space
        EntityId = space.State.Id.Value.ToString()
        EventData = JsonSerializer.Serialize(event, serializerOptions)
        OccurredAt = occurredAt
        CreatedAt = occurredAt
        UserId = actorUserId
    }

let private createUserPermissionEvent
    (actorUserId: UserId)
    (space: ValidatedSpace)
    (targetUserId: UserId)
    (granted: string list)
    (revoked: string list)
    (occurredAt: DateTime)
    =
    let event =
        SpaceEvents.spacePermissionsChanged
            actorUserId
            space.State.Id
            space.State.Name
            targetUserId
            $"User {targetUserId.Value}"
            granted
            revoked

    {
        Id = Guid.NewGuid()
        EventId = event.EventId.ToString()
        EventType = EventType.SpaceEvents Freetool.Domain.Entities.SpacePermissionsChangedEvent
        EntityType = EntityType.Space
        EntityId = space.State.Id.Value.ToString()
        EventData = JsonSerializer.Serialize(event, serializerOptions)
        OccurredAt = occurredAt
        CreatedAt = occurredAt
        UserId = actorUserId
    }

[<Fact>]
let ``AuthTypes parsing helpers parse supported tuple strings`` () =
    Assert.Equal(Some(Freetool.Application.Interfaces.User "alice"), AuthTypes.tryParseSubject "user:alice")

    Assert.Equal(
        Some(Freetool.Application.Interfaces.Space "engineering"),
        AuthTypes.tryParseSubject "space:engineering"
    )

    Assert.Equal(
        Some(Freetool.Application.Interfaces.Organization "default"),
        AuthTypes.tryParseSubject "organization:default"
    )

    Assert.Equal(
        Some(UserSetFromRelation("space", "engineering", "member")),
        AuthTypes.tryParseSubject "space:engineering#member"
    )

    Assert.True(AuthTypes.tryParseSubject "" |> Option.isNone)
    Assert.True(AuthTypes.tryParseSubject "not-a-subject" |> Option.isNone)

    let relationCases = [
        ("admin", OrganizationAdmin)
        ("member", SpaceMember)
        ("moderator", SpaceModerator)
        ("organization", SpaceOrganization)
        ("create", SpaceCreate)
        ("rename", SpaceRename)
        ("delete", SpaceDelete)
        ("add_member", SpaceAddMember)
        ("remove_member", SpaceRemoveMember)
        ("create_resource", ResourceCreate)
        ("edit_resource", ResourceEdit)
        ("delete_resource", ResourceDelete)
        ("create_app", AppCreate)
        ("edit_app", AppEdit)
        ("delete_app", AppDelete)
        ("run_app", AppRun)
        ("create_dashboard", DashboardCreate)
        ("edit_dashboard", DashboardEdit)
        ("delete_dashboard", DashboardDelete)
        ("run_dashboard", DashboardRun)
        ("create_folder", FolderCreate)
        ("edit_folder", FolderEdit)
        ("delete_folder", FolderDelete)
    ]

    for (relationName, expectedRelation) in relationCases do
        Assert.Equal(Some expectedRelation, AuthTypes.tryParseRelation relationName)

    Assert.True(AuthTypes.tryParseRelation "unknown_relation" |> Option.isNone)

    Assert.Equal(Some(UserObject "alice"), AuthTypes.tryParseObject "user:alice")
    Assert.Equal(Some(SpaceObject "engineering"), AuthTypes.tryParseObject "space:engineering")
    Assert.Equal(Some(OrganizationObject "default"), AuthTypes.tryParseObject "organization:default")
    Assert.Equal(Some(SpaceObject "engineering#member"), AuthTypes.tryParseObject "space:engineering#member")
    Assert.True(AuthTypes.tryParseObject "workspace:engineering" |> Option.isNone)
    Assert.True(AuthTypes.tryParseObject "not-an-object" |> Option.isNone)

[<Fact>]
let ``RelationshipTuple toDisplayString formats tuple identity`` () =
    let tuple = {
        Subject = UserSetFromRelation("space", "engineering", "member")
        Relation = AppRun
        Object = SpaceObject "engineering"
    }

    Assert.Equal("space:engineering#run_app@space:engineering#member", RelationshipTuple.toDisplayString tuple)

[<Fact>]
let ``relationToAuditName returns audit permission name`` () =
    Assert.Equal("RunApp", OpenFgaSpaceAuthorizationRepair.relationToAuditName AppRun)

[<Fact>]
let ``RepairAsync rebuilds persisted space tuples plus audit derived permissions`` () : Task = task {
    let actorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let space = createSpace "Engineering" [ memberUserId ]
    let spaceId = space.State.Id.Value.ToString()
    let defaultMemberSubject = UserSetFromRelation("space", spaceId, "member")

    let events = [
        createDefaultPermissionEvent actorUserId space [ "CreateApp" ] [] (DateTime.UtcNow.AddMinutes(-5.0))
        createUserPermissionEvent actorUserId space memberUserId [ "EditApp" ] [] (DateTime.UtcNow.AddMinutes(-4.0))
    ]

    let eventRepository = MockEventRepository(events) :> IEventRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()

    let service =
        OpenFgaSpaceAuthorizationRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            authService :> IAuthorizationRelationshipReader,
            NullLogger<OpenFgaSpaceAuthorizationRepairService>.Instance
        )

    let! summary = service.RepairAsync(true, None)

    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(1, summary.SpacesWithDrift)

    let result = Assert.Single(summary.Results)
    Assert.True(result.Applied)

    let request = Assert.Single(authService.UpdateRequests)

    Assert.Contains(
        request.TuplesToAdd,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.Organization "default"
            && tuple.Relation = SpaceOrganization
    )

    Assert.Contains(
        request.TuplesToAdd,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(space.State.ModeratorUserId.Value.ToString())
            && tuple.Relation = SpaceModerator
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToAdd,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(memberUserId.Value.ToString())
            && tuple.Relation = SpaceMember
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToAdd,
        fun tuple -> tuple.Subject = defaultMemberSubject && tuple.Relation = AppCreate
    )

    Assert.Contains(request.TuplesToAdd, fun tuple -> tuple.Subject = defaultMemberSubject && tuple.Relation = AppRun)

    Assert.Contains(
        request.TuplesToAdd,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(memberUserId.Value.ToString())
            && tuple.Relation = AppEdit
            && tuple.Object = SpaceObject spaceId
    )
}

[<Fact>]
let ``RepairAsync removes stale tuples that are no longer present in persisted state or audit history`` () : Task = task {
    let actorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let removedUserId = UserId.NewId()
    let oldModeratorUserId = UserId.NewId()
    let space = createSpace "Support" [ memberUserId ]
    let spaceId = space.State.Id.Value.ToString()

    let events = [
        createDefaultPermissionEvent actorUserId space [ "CreateDashboard" ] [] (DateTime.UtcNow.AddMinutes(-5.0))
        createUserPermissionEvent actorUserId space memberUserId [ "EditApp" ] [] (DateTime.UtcNow.AddMinutes(-4.0))
    ]

    let eventRepository = MockEventRepository(events) :> IEventRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()

    authService.SeedTuple(
        {
            Subject = Freetool.Application.Interfaces.User(oldModeratorUserId.Value.ToString())
            Relation = SpaceModerator
            Object = SpaceObject spaceId
        }
    )

    authService.SeedTuple(
        {
            Subject = Freetool.Application.Interfaces.User(removedUserId.Value.ToString())
            Relation = SpaceMember
            Object = SpaceObject spaceId
        }
    )

    authService.SeedTuple(
        {
            Subject = Freetool.Application.Interfaces.User(removedUserId.Value.ToString())
            Relation = AppDelete
            Object = SpaceObject spaceId
        }
    )

    authService.SeedTuple(
        {
            Subject = UserSetFromRelation("space", spaceId, "member")
            Relation = AppCreate
            Object = SpaceObject spaceId
        }
    )

    authService.SeedTuple(
        {
            Subject = Freetool.Application.Interfaces.User(memberUserId.Value.ToString())
            Relation = AppRun
            Object = SpaceObject spaceId
        }
    )

    let service =
        OpenFgaSpaceAuthorizationRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            authService :> IAuthorizationRelationshipReader,
            NullLogger<OpenFgaSpaceAuthorizationRepairService>.Instance
        )

    let! summary = service.RepairAsync(true, None)

    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(1, summary.SpacesWithDrift)

    let request = Assert.Single(authService.UpdateRequests)

    Assert.Contains(
        request.TuplesToRemove,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(oldModeratorUserId.Value.ToString())
            && tuple.Relation = SpaceModerator
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToRemove,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(removedUserId.Value.ToString())
            && tuple.Relation = SpaceMember
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToRemove,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(removedUserId.Value.ToString())
            && tuple.Relation = AppDelete
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToRemove,
        fun tuple ->
            tuple.Subject = UserSetFromRelation("space", spaceId, "member")
            && tuple.Relation = AppCreate
            && tuple.Object = SpaceObject spaceId
    )

    Assert.Contains(
        request.TuplesToRemove,
        fun tuple ->
            tuple.Subject = Freetool.Application.Interfaces.User(memberUserId.Value.ToString())
            && tuple.Relation = AppRun
            && tuple.Object = SpaceObject spaceId
    )
}

[<Fact>]
let ``RepairAsync via interface respects space filter and dry run mode`` () : Task = task {
    let actorUserId = UserId.NewId()
    let targetSpace = createSpace "Engineering" []
    let otherSpace = createSpace "Support" []
    let targetSpaceId = targetSpace.State.Id.Value.ToString()

    let events = [
        createDefaultPermissionEvent actorUserId targetSpace [ "CreateApp" ] [] (DateTime.UtcNow.AddMinutes(-5.0))
        createDefaultPermissionEvent actorUserId otherSpace [ "CreateDashboard" ] [] (DateTime.UtcNow.AddMinutes(-4.0))
    ]

    let eventRepository = MockEventRepository(events) :> IEventRepository

    let spaceRepository =
        MockSpaceRepository([ targetSpace; otherSpace ]) :> ISpaceRepository

    let authService = TrackingAuthorizationService()

    let service =
        OpenFgaSpaceAuthorizationRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            authService :> IAuthorizationRelationshipReader,
            NullLogger<OpenFgaSpaceAuthorizationRepairService>.Instance
        )
        :> IOpenFgaSpaceAuthorizationRepairService

    let! summary = service.RepairAsync false (Some targetSpaceId)

    Assert.Equal(Some targetSpaceId, summary.SpaceFilter)
    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(1, summary.SpacesWithDrift)
    Assert.Empty(authService.UpdateRequests)

    let result = Assert.Single(summary.Results)
    Assert.Equal(targetSpaceId, result.SpaceId)
    Assert.False(result.Applied)
    Assert.Contains(result.RelationshipsToAdd, fun relationship -> relationship.Contains("run_app"))

    Assert.DoesNotContain(
        result.RelationshipsToAdd,
        fun relationship -> relationship.Contains(otherSpace.State.Id.Value.ToString())
    )
}

[<Fact>]
let ``RepairAsync records warnings for unknown permission names and malformed event payloads`` () : Task = task {
    let actorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let space = createSpace "Operations" [ memberUserId ]
    let occurredAt = DateTime.UtcNow

    let defaultEvent =
        createDefaultPermissionEvent actorUserId space [ "RunApp"; "UnknownGrant" ] [ "UnknownRevoke" ] occurredAt

    let malformedUserEvent = {
        Id = Guid.NewGuid()
        EventId = Guid.NewGuid().ToString()
        EventType = EventType.SpaceEvents Freetool.Domain.Entities.SpacePermissionsChangedEvent
        EntityType = EntityType.Space
        EntityId = space.State.Id.Value.ToString()
        EventData = "{not-json"
        OccurredAt = occurredAt.AddMinutes(1.0)
        CreatedAt = occurredAt.AddMinutes(1.0)
        UserId = actorUserId
    }

    let eventRepository =
        MockEventRepository([ defaultEvent; malformedUserEvent ]) :> IEventRepository

    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()

    let service =
        OpenFgaSpaceAuthorizationRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            authService :> IAuthorizationRelationshipReader,
            NullLogger<OpenFgaSpaceAuthorizationRepairService>.Instance
        )

    let! summary = service.RepairAsync(false, None)
    let result = Assert.Single(summary.Results)

    Assert.Equal(3, result.Warnings.Length)
    Assert.Contains(result.Warnings, fun warning -> warning.Contains("UnknownGrant"))
    Assert.Contains(result.Warnings, fun warning -> warning.Contains("UnknownRevoke"))
    Assert.Contains(result.Warnings, fun warning -> warning.Contains("Failed to deserialize member permission event"))
}

[<Fact>]
let ``RepairAsync paginates through multiple event pages`` () : Task = task {
    let actorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let space = createSpace "Growth" [ memberUserId ]
    let spaceId = space.State.Id.Value.ToString()

    let events =
        [ 0..200 ]
        |> List.map (fun index ->
            createUserPermissionEvent
                actorUserId
                space
                memberUserId
                [
                    if index % 2 = 0 then "EditApp" else "DeleteDashboard"
                ]
                []
                (DateTime.UtcNow.AddMinutes(float -index)))

    let eventRepository = MockEventRepository(events) :> IEventRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()

    let service =
        OpenFgaSpaceAuthorizationRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            authService :> IAuthorizationRelationshipReader,
            NullLogger<OpenFgaSpaceAuthorizationRepairService>.Instance
        )

    let! summary = service.RepairAsync(false, Some spaceId)
    let result = Assert.Single(summary.Results)

    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(spaceId, result.SpaceId)
    Assert.Contains(result.DesiredRelationships, fun relationship -> relationship.Contains("edit_app"))
    Assert.Contains(result.DesiredRelationships, fun relationship -> relationship.Contains("delete_dashboard"))
}