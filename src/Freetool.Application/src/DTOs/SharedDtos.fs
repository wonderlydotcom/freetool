namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

module ValidationConstants =
    // Description
    [<Literal>]
    let DescriptionMaxLength = 500

    [<Literal>]
    let DescriptionMinLength = 1

    [<Literal>]
    let DescriptionErrorMessage = "Description must be between 1 and 500 characters"

    // Email
    [<Literal>]
    let EmailMaxLength = 254

    [<Literal>]
    let EmailErrorMessage = "Email cannot exceed 254 characters"

    // Input Title
    [<Literal>]
    let InputTitleMaxLength = 255

    [<Literal>]
    let InputTitleMinLength = 1

    [<Literal>]
    let InputTitleErrorMessage = "Input title must be between 1 and 255 characters"

    // Input Description
    [<Literal>]
    let InputDescriptionMaxLength = 100

    [<Literal>]
    let InputDescriptionErrorMessage = "Input description cannot exceed 100 characters"

    // Input Value
    [<Literal>]
    let InputValueMaxLength = 1_000

    [<Literal>]
    let InputValueMinLength = 1

    [<Literal>]
    let InputValueErrorMessage = "Value cannot exceed 1,000 characters"

    // Name
    [<Literal>]
    let NameMaxLength = 100

    [<Literal>]
    let NameMinLength = 1

    [<Literal>]
    let NameErrorMessage = "Name must be between 1 and 100 characters"

    // URL
    [<Literal>]
    let URLMaxLength = 2_000

    [<Literal>]
    let URLMinLength = 1

    [<Literal>]
    let URLErrorMessage = "URL cannot exceed 2000 characters"

type OptionalStringLengthAttribute(maxLength: int) =
    inherit ValidationAttribute()

    override _.IsValid(value: obj) =
        match value with
        | null -> true
        | :? (string option) as opt ->
            match opt with
            | None -> true
            | Some strValue -> strValue.Length <= maxLength
        | :? string as strValue -> strValue.Length <= maxLength
        | _ -> false


type PagedResult<'T> = {
    Items: 'T list
    TotalCount: int
    Skip: int
    Take: int
}

type InputDto = {
    [<Required>]
    [<StringLength(ValidationConstants.InputTitleMaxLength,
                   MinimumLength = ValidationConstants.InputTitleMinLength,
                   ErrorMessage = ValidationConstants.InputTitleErrorMessage)>]
    Title: string

    [<OptionalStringLength(ValidationConstants.InputDescriptionMaxLength,
                           ErrorMessage = ValidationConstants.InputDescriptionErrorMessage)>]
    Description: string option

    [<Required>]
    Type: InputTypeDto
}