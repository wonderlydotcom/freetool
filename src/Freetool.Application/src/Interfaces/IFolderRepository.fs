namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IFolderRepository =
    abstract member GetByIdAsync: FolderId -> Task<ValidatedFolder option>

    abstract member GetChildrenAsync: FolderId -> Task<ValidatedFolder list>

    abstract member GetRootFoldersAsync: skip: int -> take: int -> Task<ValidatedFolder list>

    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedFolder list>

    abstract member GetBySpaceAsync: spaceId: SpaceId -> skip: int -> take: int -> Task<ValidatedFolder list>

    abstract member AddAsync: ValidatedFolder -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedFolder -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: ValidatedFolder -> Task<Result<unit, DomainError>>

    abstract member ExistsAsync: FolderId -> Task<bool>

    abstract member ExistsByNameInParentAsync: FolderName -> FolderId option -> Task<bool>

    abstract member GetCountAsync: unit -> Task<int>

    abstract member GetCountBySpaceAsync: spaceId: SpaceId -> Task<int>

    abstract member GetBySpaceIdsAsync: spaceIds: SpaceId list -> skip: int -> take: int -> Task<ValidatedFolder list>

    abstract member GetCountBySpaceIdsAsync: spaceIds: SpaceId list -> Task<int>

    abstract member GetRootCountAsync: unit -> Task<int>

    abstract member GetChildCountAsync: FolderId -> Task<int>

    abstract member GetDeletedBySpaceAsync: SpaceId -> Task<ValidatedFolder list>

    abstract member GetDeletedByIdAsync: FolderId -> Task<ValidatedFolder option>

    abstract member RestoreWithChildrenAsync: ValidatedFolder -> Task<Result<int, DomainError>>

    abstract member CheckNameConflictAsync: FolderName -> FolderId option -> SpaceId -> Task<bool>