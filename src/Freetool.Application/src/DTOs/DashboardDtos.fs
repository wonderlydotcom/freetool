namespace Freetool.Application.DTOs

open System.ComponentModel.DataAnnotations

type CreateDashboardDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string

    [<Required>]
    FolderId: string

    PrepareAppId: string option

    [<Required>]
    Configuration: string
}

type UpdateDashboardNameDto = {
    [<Required>]
    [<StringLength(ValidationConstants.NameMaxLength,
                   MinimumLength = ValidationConstants.NameMinLength,
                   ErrorMessage = ValidationConstants.NameErrorMessage)>]
    Name: string
}

type UpdateDashboardConfigurationDto = {
    [<Required>]
    Configuration: string
}

type UpdateDashboardPrepareAppDto = { PrepareAppId: string option }

type PrepareDashboardDto = {
    [<Required>]
    LoadInputs: RunInputDto list
}

type RunDashboardActionDto = {
    PrepareRunId: string option

    [<Required>]
    LoadInputs: RunInputDto list

    [<Required>]
    ActionInputs: RunInputDto list

    PriorActionRunIds: KeyValuePairDto list option
}

type DashboardPrepareResponseDto = {
    PrepareRunId: string
    Status: string
    Response: string option
    ErrorMessage: string option
}

type DashboardActionResponseDto = {
    ActionRunId: string
    Status: string
    Response: string option
    ErrorMessage: string option
}