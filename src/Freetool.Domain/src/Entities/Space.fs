namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

/// Space entity - unified replacement for Group + Workspace
/// Each Space has exactly one Moderator (required) and zero or more Members
[<Table("Spaces")>]
[<Index([| "Name" |], IsUnique = true, Name = "IX_Spaces_Name")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type SpaceData = {
    [<Key>]
    Id: SpaceId

    [<Required>]
    [<MaxLength(100)>]
    Name: string

    /// The moderator user ID - exactly one moderator per Space, required
    [<Required>]
    ModeratorUserId: UserId

    [<Required>]
    [<JsonIgnore>]
    CreatedAt: DateTime

    [<Required>]
    [<JsonIgnore>]
    UpdatedAt: DateTime

    [<JsonIgnore>]
    IsDeleted: bool

    /// Member user IDs - populated via junction table, not stored directly
    [<NotMapped>]
    [<JsonPropertyName("memberIds")>]
    mutable MemberIds: UserId list
}

/// Junction entity for many-to-many relationship between Users and Spaces
[<Table("SpaceMembers")>]
[<Index([| "UserId"; "SpaceId" |], IsUnique = true, Name = "IX_SpaceMembers_UserId_SpaceId")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type SpaceMemberData = {
    [<Key>]
    Id: Guid

    [<Required>]
    UserId: UserId

    [<Required>]
    SpaceId: SpaceId

    [<Required>]
    [<JsonIgnore>]
    CreatedAt: DateTime
}

type Space = EventSourcingAggregate<SpaceData>

module SpaceAggregateHelpers =
    let getEntityId (space: Space) : SpaceId = space.State.Id

    let implementsIEntity (space: Space) =
        { new IEntity<SpaceId> with
            member _.Id = space.State.Id
        }

// Type aliases for clarity
type UnvalidatedSpace = Space // From DTOs - potentially unsafe
type ValidatedSpace = Space // Validated domain model and database data

module Space =
    /// Creates a new Space with a required moderator
    let create
        (actorUserId: UserId)
        (name: string)
        (moderatorUserId: UserId)
        (memberIds: UserId list option)
        : Result<ValidatedSpace, DomainError> =
        let trimmedName = name.Trim()

        match trimmedName with
        | "" -> Error(ValidationError "Space name cannot be empty")
        | nameValue when nameValue.Length > 100 -> Error(ValidationError "Space name cannot exceed 100 characters")
        | nameValue ->
            // Handle memberIds - remove duplicates and default to empty list if None
            // Also ensure moderator is not in member list (they have separate role)
            let validatedMemberIds =
                memberIds
                |> Option.defaultValue []
                |> List.distinct
                |> List.filter (fun id -> id <> moderatorUserId)

            let spaceData = {
                Id = SpaceId.NewId()
                Name = nameValue
                ModeratorUserId = moderatorUserId
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                IsDeleted = false
                MemberIds = validatedMemberIds
            }

            let spaceCreatedEvent =
                SpaceEvents.spaceCreated actorUserId spaceData.Id spaceData.Name moderatorUserId validatedMemberIds

            Ok {
                State = spaceData
                UncommittedEvents = [ spaceCreatedEvent :> IDomainEvent ]
            }

    /// Reconstructs a Space from persisted data
    let fromData (spaceData: SpaceData) : ValidatedSpace = {
        State = spaceData
        UncommittedEvents = []
    }

    /// Validates an unvalidated Space
    let validate (space: UnvalidatedSpace) : Result<ValidatedSpace, DomainError> =
        let spaceData = space.State

        match spaceData.Name with
        | "" -> Error(ValidationError "Space name cannot be empty")
        | nameValue when nameValue.Length > 100 -> Error(ValidationError "Space name cannot exceed 100 characters")
        | nameValue ->
            // Remove duplicates from MemberIds and ensure moderator is not in member list
            let distinctMemberIds =
                spaceData.MemberIds
                |> List.distinct
                |> List.filter (fun id -> id <> spaceData.ModeratorUserId)

            let updatedSpaceData = {
                spaceData with
                    Name = nameValue.Trim()
                    MemberIds = distinctMemberIds
            }

            Ok {
                State = updatedSpaceData
                UncommittedEvents = space.UncommittedEvents
            }

    /// Updates the name of a Space
    let updateName
        (actorUserId: UserId)
        (newName: string)
        (space: ValidatedSpace)
        : Result<ValidatedSpace, DomainError> =
        match newName with
        | "" -> Error(ValidationError "Space name cannot be empty")
        | nameValue when nameValue.Length > 100 -> Error(ValidationError "Space name cannot exceed 100 characters")
        | nameValue ->
            let oldName = space.State.Name
            let trimmedName = nameValue.Trim()

            if oldName = trimmedName then
                Ok space // No change needed
            else
                let updatedSpaceData = {
                    space.State with
                        Name = trimmedName
                        UpdatedAt = DateTime.UtcNow
                }

                let nameChangedEvent =
                    SpaceEvents.spaceUpdated actorUserId space.State.Id [
                        SpaceChange.NameChanged(oldName, trimmedName)
                    ]

                Ok {
                    State = updatedSpaceData
                    UncommittedEvents = space.UncommittedEvents @ [ nameChangedEvent :> IDomainEvent ]
                }

    /// Changes the moderator of a Space
    /// The old moderator becomes a regular member if they were in the member list
    let changeModerator
        (actorUserId: UserId)
        (newModeratorUserId: UserId)
        (space: ValidatedSpace)
        : Result<ValidatedSpace, DomainError> =
        let oldModeratorUserId = space.State.ModeratorUserId

        if oldModeratorUserId = newModeratorUserId then
            Ok space // No change needed
        else
            // Remove new moderator from member list if they were a member
            let updatedMemberIds =
                space.State.MemberIds |> List.filter (fun id -> id <> newModeratorUserId)

            let updatedSpaceData = {
                space.State with
                    ModeratorUserId = newModeratorUserId
                    MemberIds = updatedMemberIds
                    UpdatedAt = DateTime.UtcNow
            }

            let moderatorChangedEvent =
                SpaceEvents.spaceUpdated actorUserId space.State.Id [
                    SpaceChange.ModeratorChanged(oldModeratorUserId, newModeratorUserId)
                ]

            Ok {
                State = updatedSpaceData
                UncommittedEvents = space.UncommittedEvents @ [ moderatorChangedEvent :> IDomainEvent ]
            }

    /// Adds a member to the Space
    let addMember (actorUserId: UserId) (userId: UserId) (space: ValidatedSpace) : Result<ValidatedSpace, DomainError> =
        if userId = space.State.ModeratorUserId then
            Error(Conflict "Cannot add moderator as a member - they already have moderator role")
        elif List.contains userId space.State.MemberIds then
            Error(Conflict "User is already a member of this space")
        else
            let updatedSpaceData = {
                space.State with
                    UpdatedAt = DateTime.UtcNow
                    MemberIds = userId :: space.State.MemberIds
            }

            let memberAddedEvent =
                SpaceEvents.spaceUpdated actorUserId space.State.Id [ SpaceChange.MemberAdded(userId) ]

            Ok {
                State = updatedSpaceData
                UncommittedEvents = space.UncommittedEvents @ [ memberAddedEvent :> IDomainEvent ]
            }

    /// Removes a member from the Space
    /// Cannot remove the moderator - use changeModerator instead
    let removeMember
        (actorUserId: UserId)
        (userId: UserId)
        (space: ValidatedSpace)
        : Result<ValidatedSpace, DomainError> =
        if userId = space.State.ModeratorUserId then
            Error(
                ValidationError
                    "Cannot remove moderator from space - use changeModerator to transfer moderator role first"
            )
        elif not (List.contains userId space.State.MemberIds) then
            Error(NotFound "User is not a member of this space")
        else
            let updatedSpaceData = {
                space.State with
                    UpdatedAt = DateTime.UtcNow
                    MemberIds = List.filter (fun id -> id <> userId) space.State.MemberIds
            }

            let memberRemovedEvent =
                SpaceEvents.spaceUpdated actorUserId space.State.Id [ SpaceChange.MemberRemoved(userId) ]

            Ok {
                State = updatedSpaceData
                UncommittedEvents = space.UncommittedEvents @ [ memberRemovedEvent :> IDomainEvent ]
            }

    /// Marks a Space for deletion, generating the delete event with name for audit
    let markForDeletion (actorUserId: UserId) (space: ValidatedSpace) : ValidatedSpace =
        let spaceDeletedEvent =
            SpaceEvents.spaceDeleted actorUserId space.State.Id space.State.Name

        {
            space with
                UncommittedEvents = space.UncommittedEvents @ [ spaceDeletedEvent :> IDomainEvent ]
        }

    // Getters and utility functions

    let getUncommittedEvents (space: ValidatedSpace) : IDomainEvent list = space.UncommittedEvents

    let markEventsAsCommitted (space: ValidatedSpace) : ValidatedSpace = { space with UncommittedEvents = [] }

    let getId (space: Space) : SpaceId = space.State.Id

    let getName (space: Space) : string = space.State.Name

    let getModeratorUserId (space: Space) : UserId = space.State.ModeratorUserId

    let getMemberIds (space: Space) : UserId list = space.State.MemberIds

    let getCreatedAt (space: Space) : DateTime = space.State.CreatedAt

    let getUpdatedAt (space: Space) : DateTime = space.State.UpdatedAt

    let hasMember (userId: UserId) (space: Space) : bool =
        List.contains userId space.State.MemberIds

    let isModerator (userId: UserId) (space: Space) : bool = space.State.ModeratorUserId = userId

    /// Checks if a user is either a moderator or member of the space
    let hasAccess (userId: UserId) (space: Space) : bool =
        isModerator userId space || hasMember userId space

    /// Helper function to validate MemberIds - to be used by application layer
    let validateMemberIds (memberIds: UserId list) (userExistsFunc: UserId -> bool) : Result<UserId list, DomainError> =
        let distinctMemberIds = memberIds |> List.distinct

        let invalidMemberIds =
            distinctMemberIds |> List.filter (fun userId -> not (userExistsFunc userId))

        match invalidMemberIds with
        | [] -> Ok distinctMemberIds
        | invalidIds ->
            let invalidIdStrings = invalidIds |> List.map (fun id -> id.Value.ToString())

            let message =
                sprintf "The following user IDs do not exist or are deleted: %s" (String.concat ", " invalidIdStrings)

            Error(ValidationError message)