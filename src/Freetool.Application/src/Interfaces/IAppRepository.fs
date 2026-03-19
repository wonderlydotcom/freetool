namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IAppRepository =
    abstract member GetByIdAsync: AppId -> Task<ValidatedApp option>

    abstract member GetByNameAndFolderIdAsync: AppName -> FolderId -> Task<ValidatedApp option>

    abstract member GetByFolderIdAsync: FolderId -> skip: int -> take: int -> Task<ValidatedApp list>

    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedApp list>

    abstract member GetBySpaceIdsAsync: spaceIds: SpaceId list -> skip: int -> take: int -> Task<ValidatedApp list>

    abstract member AddAsync: ValidatedApp -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedApp -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: AppId -> UserId -> Task<Result<unit, DomainError>>

    abstract member ExistsAsync: AppId -> Task<bool>

    abstract member ExistsByNameAndFolderIdAsync: AppName -> FolderId -> Task<bool>

    abstract member GetCountAsync: unit -> Task<int>

    abstract member GetCountByFolderIdAsync: FolderId -> Task<int>

    abstract member GetCountBySpaceIdsAsync: SpaceId list -> Task<int>

    abstract member GetByResourceIdAsync: ResourceId -> Task<ValidatedApp list>

    abstract member GetDeletedByFolderIdsAsync: FolderId list -> Task<ValidatedApp list>

    abstract member GetDeletedByIdAsync: AppId -> Task<ValidatedApp option>

    abstract member RestoreAsync: ValidatedApp -> Task<Result<unit, DomainError>>

    abstract member CheckNameConflictAsync: string -> FolderId -> Task<bool>