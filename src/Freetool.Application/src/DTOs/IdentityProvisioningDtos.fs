namespace Freetool.Application.DTOs

open System
open System.ComponentModel.DataAnnotations

type IdentityGroupSpaceMappingDto = {
    Id: string
    GroupKey: string
    SpaceId: string
    SpaceName: string option
    IsActive: bool
    CreatedAt: DateTime
    UpdatedAt: DateTime
}

type CreateIdentityGroupSpaceMappingDto = {
    [<Required>]
    [<StringLength(200, MinimumLength = 1)>]
    GroupKey: string
    [<Required>]
    SpaceId: string
}

type UpdateIdentityGroupSpaceMappingDto = {
    [<Required>]
    IsActive: bool
}