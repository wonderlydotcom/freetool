namespace Freetool.Application.Mappers

open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

module ResourceMapper =
    let private keyValuePairFromDto (dto: KeyValuePairDto) : (string * string) = (dto.Key, dto.Value)

    let fromCreateHttpDto (actorUserId: UserId) (dto: CreateHttpResourceDto) : Result<ValidatedResource, DomainError> =
        let urlParameters = dto.UrlParameters |> List.map keyValuePairFromDto
        let headers = dto.Headers |> List.map keyValuePairFromDto
        let body = dto.Body |> List.map keyValuePairFromDto

        match System.Guid.TryParse dto.SpaceId with
        | true, guid ->
            let spaceId = SpaceId.FromGuid guid

            Resource.createWithKind
                actorUserId
                spaceId
                ResourceKind.Http
                dto.Name
                dto.Description
                (Some dto.BaseUrl)
                urlParameters
                headers
                body
                None
                None
                None
                None
                None
                None
                None
                false
                false
                []
        | false, _ -> Error(ValidationError "Invalid space ID format")

    let fromCreateSqlDto (actorUserId: UserId) (dto: CreateSqlResourceDto) : Result<ValidatedResource, DomainError> =
        let connectionOptions = dto.ConnectionOptions |> List.map keyValuePairFromDto

        match System.Guid.TryParse dto.SpaceId with
        | true, guid ->
            let spaceId = SpaceId.FromGuid guid

            Resource.createWithKind
                actorUserId
                spaceId
                ResourceKind.Sql
                dto.Name
                dto.Description
                None
                []
                []
                []
                (Some dto.DatabaseName)
                (Some dto.DatabaseHost)
                (Some dto.DatabasePort)
                (Some dto.DatabaseEngine)
                (Some dto.DatabaseAuthScheme)
                (Some dto.DatabaseUsername)
                dto.DatabasePassword
                dto.UseSsl
                dto.EnableSshTunnel
                connectionOptions
        | false, _ -> Error(ValidationError "Invalid space ID format")

    let fromUpdateNameDto
        (actorUserId: UserId)
        (dto: UpdateResourceNameDto)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        Resource.updateName actorUserId dto.Name resource

    let fromUpdateDescriptionDto
        (actorUserId: UserId)
        (dto: UpdateResourceDescriptionDto)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        Resource.updateDescription actorUserId dto.Description resource

    let fromUpdateBaseUrlDto
        (actorUserId: UserId)
        (dto: UpdateResourceBaseUrlDto)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        Resource.updateBaseUrl actorUserId dto.BaseUrl resource

    let fromUpdateUrlParametersDto
        (actorUserId: UserId)
        (dto: UpdateResourceUrlParametersDto)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        let urlParameters = dto.UrlParameters |> List.map keyValuePairFromDto
        Resource.updateUrlParameters actorUserId urlParameters apps resource

    let fromUpdateHeadersDto
        (actorUserId: UserId)
        (dto: UpdateResourceHeadersDto)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        let headers = dto.Headers |> List.map keyValuePairFromDto
        Resource.updateHeaders actorUserId headers apps resource

    let fromUpdateBodyDto
        (actorUserId: UserId)
        (dto: UpdateResourceBodyDto)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        let body = dto.Body |> List.map keyValuePairFromDto
        Resource.updateBody actorUserId body apps resource

    let fromUpdateDatabaseConfigDto
        (actorUserId: UserId)
        (dto: UpdateResourceDatabaseConfigDto)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        let connectionOptions = dto.ConnectionOptions |> List.map keyValuePairFromDto

        Resource.updateDatabaseConfig
            actorUserId
            dto.DatabaseName
            dto.DatabaseHost
            dto.DatabasePort
            dto.DatabaseEngine
            dto.DatabaseAuthScheme
            dto.DatabaseUsername
            dto.DatabasePassword
            dto.UseSsl
            dto.EnableSshTunnel
            connectionOptions
            resource