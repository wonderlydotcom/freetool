namespace Freetool.Application.Interfaces

open System
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type IIdentityGroupSpaceMappingRepository =
    abstract member GetAllAsync: unit -> Task<IdentityGroupSpaceMappingDto list>

    abstract member GetSpaceIdsByGroupKeysAsync: groupKeys: string list -> Task<SpaceId list>

    abstract member AddAsync:
        actorUserId: UserId ->
        groupKey: string ->
        spaceId: SpaceId ->
            Task<Result<IdentityGroupSpaceMappingDto, DomainError>>

    abstract member UpdateIsActiveAsync:
        actorUserId: UserId -> mappingId: Guid -> isActive: bool -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: mappingId: Guid -> Task<Result<unit, DomainError>>