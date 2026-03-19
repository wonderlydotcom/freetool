namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations
open System.Text.Json.Serialization

type CreateUserDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    [<Required>]
    [<StringLength(ValidationConstants.EmailMaxLength, ErrorMessage = ValidationConstants.EmailErrorMessage)>]
    [<EmailAddress(ErrorMessage = "Invalid email format")>]
    Email: string

    [<StringLength(ValidationConstants.URLMaxLength, ErrorMessage = ValidationConstants.URLErrorMessage)>]
    [<Url(ErrorMessage = "Profile picture URL must be a valid URL")>]
    [<JsonConverter(typeof<StringOptionConverter>)>]
    ProfilePicUrl: string option
}

type UpdateUserNameDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string
}

type UpdateUserEmailDto = {
    [<Required>]
    [<StringLength(ValidationConstants.EmailMaxLength, ErrorMessage = ValidationConstants.EmailErrorMessage)>]
    [<EmailAddress(ErrorMessage = "Invalid email format")>]
    Email: string
}

type SetProfilePictureDto = {
    [<Required>]
    [<StringLength(ValidationConstants.URLMaxLength,
                   MinimumLength = ValidationConstants.URLMinLength,
                   ErrorMessage = ValidationConstants.URLErrorMessage)>]
    [<Url(ErrorMessage = "Profile picture URL must be a valid URL")>]
    ProfilePicUrl: string
}

type InviteUserDto = {
    [<Required>]
    [<StringLength(ValidationConstants.EmailMaxLength, ErrorMessage = ValidationConstants.EmailErrorMessage)>]
    [<EmailAddress(ErrorMessage = "Invalid email format")>]
    Email: string
}

/// Represents a user's membership in a team/group
type TeamMembershipDto = {
    Id: string
    Name: string
    Role: string
} // "admin" or "member"

/// Response DTO for GET /user/me endpoint
type CurrentUserDto = {
    Id: string
    Name: string
    Email: string

    [<JsonConverter(typeof<StringOptionConverter>)>]
    ProfilePicUrl: string option
    IsOrgAdmin: bool
    Teams: TeamMembershipDto list
}

/// User DTO with role information for list views
type UserWithRoleDto = {
    Id: string
    Name: string
    Email: string

    [<JsonConverter(typeof<StringOptionConverter>)>]
    ProfilePicUrl: string option
    InvitedAt: System.DateTime option
    IsOrgAdmin: bool
}