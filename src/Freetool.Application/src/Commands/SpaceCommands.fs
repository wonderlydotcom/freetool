namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

/// Result type for Space commands
type SpaceCommandResult =
    | SpaceResult of SpaceData
    | SpacesResult of PagedResult<SpaceData>
    | SpaceMembersPermissionsResult of SpaceMembersPermissionsResponseDto
    | SpaceDefaultMemberPermissionsResult of SpaceDefaultMemberPermissionsResponseDto
    | UnitResult of unit

/// Commands for Space operations
type SpaceCommand =
    /// Creates a new Space with required moderator and optional members
    | CreateSpace of actorUserId: UserId * CreateSpaceDto
    /// Gets a Space by its ID
    | GetSpaceById of spaceId: string
    /// Gets a Space by its name
    | GetSpaceByName of name: string
    /// Gets all Spaces with pagination
    | GetAllSpaces of skip: int * take: int
    /// Gets all Spaces that a user is a member of or moderator of
    | GetSpacesByUserId of userId: string * skip: int * take: int
    /// Deletes a Space
    | DeleteSpace of actorUserId: UserId * spaceId: string
    /// Updates a Space's name
    | UpdateSpaceName of actorUserId: UserId * spaceId: string * UpdateSpaceNameDto
    /// Changes the moderator of a Space
    | ChangeModerator of actorUserId: UserId * spaceId: string * ChangeModeratorDto
    /// Adds a member to a Space
    | AddMember of actorUserId: UserId * spaceId: string * AddMemberDto
    /// Removes a member from a Space
    | RemoveMember of actorUserId: UserId * spaceId: string * RemoveMemberDto
    /// Gets all space members with their permissions
    | GetSpaceMembersWithPermissions of spaceId: string * skip: int * take: int
    /// Updates a user's permissions in a Space
    | UpdateUserPermissions of actorUserId: UserId * spaceId: string * UpdateUserPermissionsDto
    /// Gets default permissions applied to all members of a space
    | GetDefaultMemberPermissions of spaceId: string
    /// Updates default permissions applied to all members of a space
    | UpdateDefaultMemberPermissions of actorUserId: UserId * spaceId: string * UpdateDefaultMemberPermissionsDto