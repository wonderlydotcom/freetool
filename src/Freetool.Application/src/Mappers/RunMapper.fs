namespace Freetool.Application.Mappers

open Freetool.Domain
open Freetool.Application.DTOs

module RunMapper =
    let runInputValueFromDto (dto: RunInputDto) : RunInputValue = { Title = dto.Title; Value = dto.Value }

    let runInputValueToDto (inputValue: RunInputValue) : RunInputDto = {
        Title = inputValue.Title
        Value = inputValue.Value
    }

    let executableHttpRequestToDto (request: ExecutableHttpRequest) : ExecutableHttpRequestDto = {
        BaseUrl = request.BaseUrl
        UrlParameters = request.UrlParameters |> List.map (fun (k, v) -> { Key = k; Value = v })
        Headers = request.Headers |> List.map (fun (k, v) -> { Key = k; Value = v })
        Body = request.Body |> List.map (fun (k, v) -> { Key = k; Value = v })
        HttpMethod = request.HttpMethod
        UseJsonBody = request.UseJsonBody
    }

    let fromCreateDto (dto: CreateRunDto) : RunInputValue list =
        dto.InputValues |> List.map runInputValueFromDto