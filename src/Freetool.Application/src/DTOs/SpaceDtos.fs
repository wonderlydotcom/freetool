namespace Freetool.Application.DTOs

open System
open System.ComponentModel.DataAnnotations

/// DTO for creating a new Space
type CreateSpaceDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    /// The user ID of the moderator (required - exactly one per Space)
    [<Required>]
    ModeratorUserId: string

    /// Optional list of member user IDs
    MemberIds: string list option
}

/// DTO for updating a Space's name
type UpdateSpaceNameDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string
}

/// DTO for changing the moderator of a Space
type ChangeModeratorDto = {
    [<Required>]
    NewModeratorUserId: string
}

/// DTO for adding a member to a Space
type AddMemberDto = {
    [<Required>]
    UserId: string
}

/// DTO for removing a member from a Space
type RemoveMemberDto = {
    [<Required>]
    UserId: string
}

/// DTO for Space API responses
type SpaceDto = {
    Id: string
    Name: string
    ModeratorUserId: string
    MemberIds: string list
    CreatedAt: DateTime
    UpdatedAt: DateTime
}