namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

type RunInputDto = {
    [<Required>]
    [<StringLength(ValidationConstants.InputTitleMaxLength,
                   MinimumLength = ValidationConstants.InputTitleMinLength,
                   ErrorMessage = ValidationConstants.InputTitleErrorMessage)>]
    Title: string

    [<Required>]
    [<StringLength(ValidationConstants.InputValueMaxLength, ErrorMessage = ValidationConstants.InputValueErrorMessage)>]
    Value: string
}

type CreateRunDto = {
    [<Required>]
    InputValues: RunInputDto list

    DynamicBody: KeyValuePairDto list option
}

// Output DTOs
type ExecutableHttpRequestDto = {
    BaseUrl: string
    UrlParameters: KeyValuePairDto list
    Headers: KeyValuePairDto list
    Body: KeyValuePairDto list
    HttpMethod: string
    UseJsonBody: bool
}