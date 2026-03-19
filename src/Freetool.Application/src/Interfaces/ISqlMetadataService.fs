namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Application.DTOs

type ISqlMetadataService =
    abstract member GetTablesAsync: ResourceData -> Task<Result<SqlTableInfoDto list, DomainError>>

    abstract member GetColumnsAsync:
        ResourceData -> tableName: string -> Task<Result<SqlColumnInfoDto list, DomainError>>