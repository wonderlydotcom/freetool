namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type ResourceCommandResult =
    | ResourceResult of ResourceData
    | ResourcesResult of PagedResult<ResourceData>
    | ResourceUnitResult of unit

type ResourceCommand =
    | CreateResource of actorUserId: UserId * ValidatedResource
    | GetResourceById of resourceId: string
    | GetAllResources of spaceId: SpaceId * skip: int * take: int
    | DeleteResource of actorUserId: UserId * resourceId: string
    | UpdateResourceName of actorUserId: UserId * resourceId: string * UpdateResourceNameDto
    | UpdateResourceDescription of actorUserId: UserId * resourceId: string * UpdateResourceDescriptionDto
    | UpdateResourceBaseUrl of actorUserId: UserId * resourceId: string * UpdateResourceBaseUrlDto
    | UpdateResourceUrlParameters of actorUserId: UserId * resourceId: string * UpdateResourceUrlParametersDto
    | UpdateResourceHeaders of actorUserId: UserId * resourceId: string * UpdateResourceHeadersDto
    | UpdateResourceBody of actorUserId: UserId * resourceId: string * UpdateResourceBodyDto
    | UpdateResourceDatabaseConfig of actorUserId: UserId * resourceId: string * UpdateResourceDatabaseConfigDto