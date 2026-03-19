namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type IUserRepository =
    abstract member GetByIdAsync: UserId -> Task<ValidatedUser option>

    abstract member GetByEmailAsync: Email -> Task<ValidatedUser option>

    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedUser list>

    abstract member AddAsync: ValidatedUser -> Task<Result<unit, DomainError>>

    abstract member UpdateAsync: ValidatedUser -> Task<Result<unit, DomainError>>

    abstract member DeleteAsync: ValidatedUser -> Task<Result<unit, DomainError>>

    abstract member ExistsAsync: UserId -> Task<bool>

    abstract member ExistsByEmailAsync: Email -> Task<bool>

    abstract member GetCountAsync: unit -> Task<int>