namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.Entities

type ISqlExecutionService =
    abstract member ExecuteQueryAsync: ResourceData -> SqlQuery -> Task<Result<string, DomainError>>