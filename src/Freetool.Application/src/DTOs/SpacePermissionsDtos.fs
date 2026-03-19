namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

/// DTO containing all space permissions as boolean fields
type SpacePermissionsDto = {
    CreateResource: bool
    EditResource: bool
    DeleteResource: bool
    CreateApp: bool
    EditApp: bool
    DeleteApp: bool
    RunApp: bool
    CreateDashboard: bool
    EditDashboard: bool
    DeleteDashboard: bool
    RunDashboard: bool
    CreateFolder: bool
    EditFolder: bool
    DeleteFolder: bool
}

/// DTO for a space member with their permissions
type SpaceMemberPermissionsDto = {
    UserId: string
    UserName: string
    UserEmail: string
    ProfilePicUrl: string option
    IsModerator: bool
    IsOrgAdmin: bool
    Permissions: SpacePermissionsDto
}

/// Response DTO containing all space members with their permissions
type SpaceMembersPermissionsResponseDto = {
    SpaceId: string
    SpaceName: string
    Members: PagedResult<SpaceMemberPermissionsDto>
}

/// DTO for updating a user's permissions in a space
type UpdateUserPermissionsDto = {
    [<Required>]
    UserId: string
    [<Required>]
    Permissions: SpacePermissionsDto
}

/// Response DTO containing default permissions applied to all non-moderator members of a space
type SpaceDefaultMemberPermissionsResponseDto = {
    SpaceId: string
    SpaceName: string
    Permissions: SpacePermissionsDto
}

/// DTO for updating default member permissions in a space
type UpdateDefaultMemberPermissionsDto = {
    [<Required>]
    Permissions: SpacePermissionsDto
}