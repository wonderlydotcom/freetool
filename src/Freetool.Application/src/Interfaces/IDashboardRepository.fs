namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IDashboardRepository =
    abstract member GetByIdAsync: DashboardId -> Task<ValidatedDashboard option>

    abstract member GetByFolderIdAsync: FolderId -> skip: int -> take: int -> Task<ValidatedDashboard list>

    abstract member GetBySpaceIdsAsync:
        spaceIds: SpaceId list -> skip: int -> take: int -> Task<ValidatedDashboard list>

    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedDashboard list>

    abstract member AddAsync: ValidatedDashboard -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedDashboard -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: DashboardId -> UserId -> Task<Result<unit, DomainError>>

    abstract member ExistsByNameAndFolderIdAsync: DashboardName -> FolderId -> Task<bool>

    abstract member GetCountByFolderIdAsync: FolderId -> Task<int>

    abstract member GetCountBySpaceIdsAsync: SpaceId list -> Task<int>

    abstract member GetCountAsync: unit -> Task<int>