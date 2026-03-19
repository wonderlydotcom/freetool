namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

type KeyValuePairDto = {
    // Intentionally not moved to SharedDtos yet - this is only the first usage
    [<Required>]
    [<StringLength(100, MinimumLength = 1, ErrorMessage = "Key must be between 1 and 100 characters")>]
    Key: string

    [<Required>]
    [<StringLength(ValidationConstants.InputValueMaxLength, ErrorMessage = ValidationConstants.InputValueErrorMessage)>]
    Value: string
}

type CreateHttpResourceDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    [<Required>]
    [<StringLength(ValidationConstants.DescriptionMaxLength,
                   MinimumLength = ValidationConstants.DescriptionMinLength,
                   ErrorMessage = ValidationConstants.DescriptionErrorMessage)>]
    Description: string

    [<Required>]
    SpaceId: string

    [<Required>]
    [<StringLength(ValidationConstants.InputValueMaxLength,
                   MinimumLength = ValidationConstants.InputValueMinLength,
                   ErrorMessage = ValidationConstants.InputValueErrorMessage)>]
    [<Url(ErrorMessage = "Base URL must be a valid URL")>]
    BaseUrl: string

    UrlParameters: KeyValuePairDto list

    Headers: KeyValuePairDto list

    Body: KeyValuePairDto list
}

type CreateSqlResourceDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    [<Required>]
    [<StringLength(ValidationConstants.DescriptionMaxLength,
                   MinimumLength = ValidationConstants.DescriptionMinLength,
                   ErrorMessage = ValidationConstants.DescriptionErrorMessage)>]
    Description: string

    [<Required>]
    SpaceId: string

    [<Required>]
    [<StringLength(200, ErrorMessage = "Database name cannot exceed 200 characters")>]
    DatabaseName: string

    [<Required>]
    [<StringLength(255, ErrorMessage = "Database host cannot exceed 255 characters")>]
    DatabaseHost: string

    [<Required>]
    DatabasePort: int

    [<Required>]
    [<StringLength(50, ErrorMessage = "Database engine cannot exceed 50 characters")>]
    DatabaseEngine: string

    [<Required>]
    [<StringLength(50, ErrorMessage = "Database auth scheme cannot exceed 50 characters")>]
    DatabaseAuthScheme: string

    [<Required>]
    [<StringLength(128, ErrorMessage = "Database username cannot exceed 128 characters")>]
    DatabaseUsername: string

    [<OptionalStringLength(256, ErrorMessage = "Database password cannot exceed 256 characters")>]
    DatabasePassword: string option

    UseSsl: bool

    EnableSshTunnel: bool

    ConnectionOptions: KeyValuePairDto list
}

type UpdateResourceNameDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string
}

type UpdateResourceDescriptionDto = {
    [<Required>]
    [<StringLength(ValidationConstants.DescriptionMaxLength,
                   MinimumLength = ValidationConstants.DescriptionMinLength,
                   ErrorMessage = ValidationConstants.DescriptionErrorMessage)>]
    Description: string
}

type UpdateResourceBaseUrlDto = {
    [<Required>]
    [<StringLength(ValidationConstants.InputValueMaxLength,
                   MinimumLength = ValidationConstants.InputValueMinLength,
                   ErrorMessage = ValidationConstants.InputValueErrorMessage)>]
    [<Url(ErrorMessage = "Base URL must be a valid URL")>]
    BaseUrl: string
}

type UpdateResourceUrlParametersDto = { UrlParameters: KeyValuePairDto list }

type UpdateResourceHeadersDto = { Headers: KeyValuePairDto list }

type UpdateResourceBodyDto = { Body: KeyValuePairDto list }

type UpdateResourceDatabaseConfigDto = {
    [<Required>]
    [<StringLength(200, ErrorMessage = "Database name cannot exceed 200 characters")>]
    DatabaseName: string

    [<Required>]
    [<StringLength(255, ErrorMessage = "Database host cannot exceed 255 characters")>]
    DatabaseHost: string

    [<Required>]
    DatabasePort: int

    [<Required>]
    [<StringLength(50, ErrorMessage = "Database engine cannot exceed 50 characters")>]
    DatabaseEngine: string

    [<Required>]
    [<StringLength(50, ErrorMessage = "Database auth scheme cannot exceed 50 characters")>]
    DatabaseAuthScheme: string

    [<Required>]
    [<StringLength(128, ErrorMessage = "Database username cannot exceed 128 characters")>]
    DatabaseUsername: string

    [<OptionalStringLength(256, ErrorMessage = "Database password cannot exceed 256 characters")>]
    DatabasePassword: string option

    UseSsl: bool

    EnableSshTunnel: bool

    ConnectionOptions: KeyValuePairDto list
}