namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

type AppInputDto = {
    [<Required>]
    Input: InputDto

    [<Required>]
    Required: bool

    DefaultValue: string option
}

type SqlFilterDto = {
    [<Required>]
    Column: string

    [<Required>]
    Operator: string

    Value: string option
}

type SqlOrderByDto = {
    [<Required>]
    Column: string

    [<Required>]
    Direction: string
}

type SqlQueryConfigDto = {
    [<Required>]
    Mode: string

    Table: string option

    Columns: string list

    Filters: SqlFilterDto list

    Limit: int option

    OrderBy: SqlOrderByDto list

    RawSql: string option

    RawSqlParams: KeyValuePairDto list
}

type CreateAppDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    [<Required>]
    FolderId: string

    [<Required>]
    ResourceId: string

    [<Required>]
    [<StringLength(10, MinimumLength = 1, ErrorMessage = "HTTP method must be between 1 and 10 characters")>]
    HttpMethod: string

    Inputs: AppInputDto list

    // Intentionally not moved to SharedDtos yet - this is only the first usage
    UrlPath: string option

    UrlParameters: KeyValuePairDto list

    Headers: KeyValuePairDto list

    Body: KeyValuePairDto list

    UseDynamicJsonBody: bool

    SqlConfig: SqlQueryConfigDto option

    Description: string option
}

type UpdateAppNameDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string
}

type UpdateAppInputsDto = {
    [<Required>]
    Inputs: AppInputDto list
}

type UpdateAppQueryParametersDto = { UrlParameters: KeyValuePairDto list }

type UpdateAppBodyDto = { Body: KeyValuePairDto list }

type UpdateAppHeadersDto = { Headers: KeyValuePairDto list }

type UpdateAppUrlPathDto = { UrlPath: string option }

type UpdateAppHttpMethodDto = {
    [<Required>]
    [<StringLength(10, MinimumLength = 1, ErrorMessage = "HTTP method must be between 1 and 10 characters")>]
    HttpMethod: string
}

type UpdateAppUseDynamicJsonBodyDto = { UseDynamicJsonBody: bool }

type UpdateAppSqlConfigDto = { SqlConfig: SqlQueryConfigDto option }

type UpdateAppDescriptionDto = { Description: string option }