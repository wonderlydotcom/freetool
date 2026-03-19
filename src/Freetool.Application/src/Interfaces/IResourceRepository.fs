namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IResourceRepository =
    abstract member GetByIdAsync: ResourceId -> Task<ValidatedResource option>

    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedResource list>

    abstract member GetBySpaceAsync: SpaceId -> skip: int -> take: int -> Task<ValidatedResource list>

    abstract member GetCountBySpaceAsync: SpaceId -> Task<int>

    abstract member AddAsync: ValidatedResource -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedResource -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: ValidatedResource -> Task<Result<unit, DomainError>>

    abstract member ExistsAsync: ResourceId -> Task<bool>

    abstract member ExistsByNameAsync: ResourceName -> Task<bool>

    abstract member GetCountAsync: unit -> Task<int>

    abstract member GetDeletedBySpaceAsync: SpaceId -> Task<ValidatedResource list>

    abstract member GetDeletedByIdAsync: ResourceId -> Task<ValidatedResource option>

    abstract member RestoreAsync: ValidatedResource -> Task<Result<unit, DomainError>>

    abstract member CheckNameConflictAsync: ResourceName -> SpaceId -> Task<bool>