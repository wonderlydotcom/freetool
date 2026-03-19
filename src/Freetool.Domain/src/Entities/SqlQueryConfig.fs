namespace Freetool.Domain.Entities

open Freetool.Domain.ValueObjects

type SqlQueryMode =
    | Gui
    | Raw

type SqlFilterOperator =
    | Equals
    | NotEquals
    | GreaterThan
    | GreaterThanOrEqual
    | LessThan
    | LessThanOrEqual
    | Like
    | ILike
    | In
    | NotIn
    | IsNull
    | IsNotNull

type SqlSortDirection =
    | Asc
    | Desc

type SqlFilter = {
    Column: string
    Operator: SqlFilterOperator
    Value: string option
}

type SqlOrderBy = {
    Column: string
    Direction: SqlSortDirection
}

type SqlQueryConfig = {
    Mode: SqlQueryMode
    Table: string option
    Columns: string list
    Filters: SqlFilter list
    Limit: int option
    OrderBy: SqlOrderBy list
    RawSql: string option
    RawSqlParams: KeyValuePair list
}