namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IRunRepository =
    abstract member GetByIdAsync: RunId -> Task<ValidatedRun option>

    abstract member GetByAppIdAsync: AppId -> skip: int -> take: int -> Task<ValidatedRun list>

    abstract member GetByStatusAsync: RunStatus -> skip: int -> take: int -> Task<ValidatedRun list>

    abstract member GetByAppIdAndStatusAsync: AppId -> RunStatus -> skip: int -> take: int -> Task<ValidatedRun list>

    abstract member AddAsync: ValidatedRun -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedRun -> Task<Result<unit, DomainError>>

    abstract member ExistsAsync: RunId -> Task<bool>

    abstract member GetCountAsync: unit -> Task<int>

    abstract member GetCountByAppIdAsync: AppId -> Task<int>

    abstract member GetCountByStatusAsync: RunStatus -> Task<int>

    abstract member GetCountByAppIdAndStatusAsync: AppId -> RunStatus -> Task<int>