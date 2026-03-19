namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

[<Table("Resources")>]
[<Index([| "Name"; "SpaceId" |], IsUnique = true, Name = "IX_Resources_Name_SpaceId")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type ResourceData = {
    [<Key>]
    Id: ResourceId

    [<Required>]
    [<MaxLength(100)>]
    Name: ResourceName

    [<Required>]
    [<MaxLength(500)>]
    Description: ResourceDescription

    [<Required>]
    SpaceId: SpaceId

    [<Required>]
    ResourceKind: ResourceKind

    [<MaxLength(1_000)>]
    BaseUrl: BaseUrl option

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON string
    UrlParameters: KeyValuePair list

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON string
    Headers: KeyValuePair list

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON string
    Body: KeyValuePair list

    [<MaxLength(200)>]
    DatabaseName: DatabaseName option

    [<MaxLength(255)>]
    DatabaseHost: DatabaseHost option

    DatabasePort: DatabasePort option

    DatabaseEngine: DatabaseEngine option

    DatabaseAuthScheme: DatabaseAuthScheme option

    [<MaxLength(128)>]
    DatabaseUsername: DatabaseUsername option

    [<MaxLength(256)>]
    DatabasePassword: DatabasePassword option

    UseSsl: bool

    EnableSshTunnel: bool

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON string
    ConnectionOptions: KeyValuePair list

    [<Required>]
    [<JsonIgnore>]
    CreatedAt: DateTime

    [<Required>]
    [<JsonIgnore>]
    UpdatedAt: DateTime

    [<JsonIgnore>]
    IsDeleted: bool
}

type Resource = EventSourcingAggregate<ResourceData>

module ResourceAggregateHelpers =
    let getEntityId (resource: Resource) : ResourceId = resource.State.Id

    let implementsIEntity (resource: Resource) =
        { new IEntity<ResourceId> with
            member _.Id = resource.State.Id
        }

// Type aliases for clarity
type UnvalidatedResource = Resource // From DTOs - potentially unsafe
type ValidatedResource = Resource // Validated domain model and database data

type HttpResourceConfig = {
    BaseUrl: BaseUrl
    UrlParameters: KeyValuePair list
    Headers: KeyValuePair list
    Body: KeyValuePair list
}

type DatabaseResourceConfig = {
    DatabaseName: DatabaseName
    Host: DatabaseHost
    Port: DatabasePort
    Engine: DatabaseEngine
    AuthScheme: DatabaseAuthScheme
    Username: DatabaseUsername
    Password: DatabasePassword
    UseSsl: bool
    EnableSshTunnel: bool
    ConnectionOptions: KeyValuePair list
}

module Resource =
    let fromData (resourceData: ResourceData) : ValidatedResource = {
        State = resourceData
        UncommittedEvents = []
    }

    let createWithKind
        (actorUserId: UserId)
        (spaceId: SpaceId)
        (resourceKind: ResourceKind)
        (name: string)
        (description: string)
        (baseUrl: string option)
        (urlParameters: (string * string) list)
        (headers: (string * string) list)
        (body: (string * string) list)
        (databaseName: string option)
        (databaseHost: string option)
        (databasePort: int option)
        (databaseEngine: string option)
        (databaseAuthScheme: string option)
        (databaseUsername: string option)
        (databasePassword: string option)
        (useSsl: bool)
        (enableSshTunnel: bool)
        (connectionOptions: (string * string) list)
        : Result<ValidatedResource, DomainError> =
        // Validate name
        match ResourceName.Create(Some name) with
        | Error err -> Error err
        | Ok validName ->
            // Validate description
            match ResourceDescription.Create(Some description) with
            | Error err -> Error err
            | Ok validDescription ->
                let validateKeyValuePairs pairs =
                    pairs
                    |> List.fold
                        (fun acc (key: string, value: string) ->
                            match acc with
                            | Error err -> Error err
                            | Ok validPairs ->
                                match KeyValuePair.Create(key, value) with
                                | Error err -> Error err
                                | Ok validPair -> Ok(validPair :: validPairs))
                        (Ok [])
                    |> Result.map List.rev

                let validateHttpConfig () : Result<HttpResourceConfig, DomainError> =
                    match baseUrl with
                    | None -> Error(ValidationError "Base URL is required for HTTP resources")
                    | Some rawBaseUrl ->
                        match BaseUrl.Create(Some rawBaseUrl) with
                        | Error err -> Error err
                        | Ok validBaseUrl ->
                            match validateKeyValuePairs urlParameters with
                            | Error err -> Error err
                            | Ok validUrlParams ->
                                match validateKeyValuePairs headers with
                                | Error err -> Error err
                                | Ok validHeaders ->
                                    match validateKeyValuePairs body with
                                    | Error err -> Error err
                                    | Ok validBody ->
                                        Ok {
                                            BaseUrl = validBaseUrl
                                            UrlParameters = validUrlParams
                                            Headers = validHeaders
                                            Body = validBody
                                        }

                let validateDatabaseConfig () : Result<DatabaseResourceConfig, DomainError> =
                    match databaseName with
                    | None -> Error(ValidationError "Database name is required for SQL resources")
                    | Some _ ->
                        match DatabaseName.Create(databaseName) with
                        | Error err -> Error err
                        | Ok validDatabaseName ->
                            match DatabaseHost.Create(databaseHost) with
                            | Error err -> Error err
                            | Ok validDatabaseHost ->
                                match DatabasePort.Create(databasePort) with
                                | Error err -> Error err
                                | Ok validDatabasePort ->
                                    match databaseEngine with
                                    | None -> Error(ValidationError "Database engine is required for SQL resources")
                                    | Some rawEngine ->
                                        match DatabaseEngine.Create(rawEngine) with
                                        | Error err -> Error err
                                        | Ok validEngine ->
                                            match databaseAuthScheme with
                                            | None ->
                                                Error(
                                                    ValidationError "Database auth scheme is required for SQL resources"
                                                )
                                            | Some rawScheme ->
                                                match DatabaseAuthScheme.Create(rawScheme) with
                                                | Error err -> Error err
                                                | Ok validAuthScheme ->
                                                    match DatabaseUsername.Create(databaseUsername) with
                                                    | Error err -> Error err
                                                    | Ok validUsername ->
                                                        match DatabasePassword.Create(databasePassword) with
                                                        | Error err -> Error err
                                                        | Ok validPassword ->
                                                            match validateKeyValuePairs connectionOptions with
                                                            | Error err -> Error err
                                                            | Ok validOptions ->
                                                                Ok {
                                                                    DatabaseName = validDatabaseName
                                                                    Host = validDatabaseHost
                                                                    Port = validDatabasePort
                                                                    Engine = validEngine
                                                                    AuthScheme = validAuthScheme
                                                                    Username = validUsername
                                                                    Password = validPassword
                                                                    UseSsl = useSsl
                                                                    EnableSshTunnel = enableSshTunnel
                                                                    ConnectionOptions = validOptions
                                                                }

                let now = DateTime.UtcNow

                match resourceKind with
                | ResourceKind.Http ->
                    match validateHttpConfig () with
                    | Error err -> Error err
                    | Ok httpConfig ->
                        if
                            databaseName.IsSome
                            || databaseHost.IsSome
                            || databasePort.IsSome
                            || databaseEngine.IsSome
                            || databaseAuthScheme.IsSome
                            || databaseUsername.IsSome
                            || databasePassword.IsSome
                            || useSsl
                            || enableSshTunnel
                            || not connectionOptions.IsEmpty
                        then
                            Error(ValidationError "Database fields are not allowed for HTTP resources")
                        else
                            let resourceData = {
                                Id = ResourceId.NewId()
                                Name = validName
                                Description = validDescription
                                SpaceId = spaceId
                                ResourceKind = ResourceKind.Http
                                BaseUrl = Some httpConfig.BaseUrl
                                UrlParameters = httpConfig.UrlParameters
                                Headers = httpConfig.Headers
                                Body = httpConfig.Body
                                DatabaseName = None
                                DatabaseHost = None
                                DatabasePort = None
                                DatabaseEngine = None
                                DatabaseAuthScheme = None
                                DatabaseUsername = None
                                DatabasePassword = None
                                UseSsl = false
                                EnableSshTunnel = false
                                ConnectionOptions = []
                                CreatedAt = now
                                UpdatedAt = now
                                IsDeleted = false
                            }

                            let resourceCreatedEvent =
                                ResourceEvents.resourceCreated
                                    actorUserId
                                    resourceData.Id
                                    validName
                                    validDescription
                                    spaceId
                                    resourceData.BaseUrl
                                    resourceData.UrlParameters
                                    resourceData.Headers
                                    resourceData.Body

                            Ok {
                                State = resourceData
                                UncommittedEvents = [ resourceCreatedEvent :> IDomainEvent ]
                            }
                | ResourceKind.Sql ->
                    match validateDatabaseConfig () with
                    | Error err -> Error err
                    | Ok databaseConfig ->
                        if
                            baseUrl.IsSome
                            || not urlParameters.IsEmpty
                            || not headers.IsEmpty
                            || not body.IsEmpty
                        then
                            Error(ValidationError "HTTP fields are not allowed for SQL resources")
                        else
                            let resourceData = {
                                Id = ResourceId.NewId()
                                Name = validName
                                Description = validDescription
                                SpaceId = spaceId
                                ResourceKind = ResourceKind.Sql
                                BaseUrl = None
                                UrlParameters = []
                                Headers = []
                                Body = []
                                DatabaseName = Some databaseConfig.DatabaseName
                                DatabaseHost = Some databaseConfig.Host
                                DatabasePort = Some databaseConfig.Port
                                DatabaseEngine = Some databaseConfig.Engine
                                DatabaseAuthScheme = Some databaseConfig.AuthScheme
                                DatabaseUsername = Some databaseConfig.Username
                                DatabasePassword = Some databaseConfig.Password
                                UseSsl = databaseConfig.UseSsl
                                EnableSshTunnel = databaseConfig.EnableSshTunnel
                                ConnectionOptions = databaseConfig.ConnectionOptions
                                CreatedAt = now
                                UpdatedAt = now
                                IsDeleted = false
                            }

                            let resourceCreatedEvent =
                                ResourceEvents.resourceCreated
                                    actorUserId
                                    resourceData.Id
                                    validName
                                    validDescription
                                    spaceId
                                    resourceData.BaseUrl
                                    resourceData.UrlParameters
                                    resourceData.Headers
                                    resourceData.Body

                            Ok {
                                State = resourceData
                                UncommittedEvents = [ resourceCreatedEvent :> IDomainEvent ]
                            }

    let create
        (actorUserId: UserId)
        (spaceId: SpaceId)
        (name: string)
        (description: string)
        (baseUrl: string)
        (urlParameters: (string * string) list)
        (headers: (string * string) list)
        (body: (string * string) list)
        : Result<ValidatedResource, DomainError> =
        createWithKind
            actorUserId
            spaceId
            ResourceKind.Http
            name
            description
            (Some baseUrl)
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

    let updateName
        (actorUserId: UserId)
        (newName: string)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match ResourceName.Create(Some newName) with
        | Error err -> Error err
        | Ok validName ->
            let oldName = resource.State.Name

            let updatedResourceData = {
                resource.State with
                    Name = validName
                    UpdatedAt = DateTime.UtcNow
            }

            let nameChangedEvent =
                ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                    ResourceChange.NameChanged(oldName, validName)
                ]

            Ok {
                State = updatedResourceData
                UncommittedEvents = resource.UncommittedEvents @ [ nameChangedEvent :> IDomainEvent ]
            }

    let updateDescription
        (actorUserId: UserId)
        (newDescription: string)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match ResourceDescription.Create(Some newDescription) with
        | Error err -> Error err
        | Ok validDescription ->
            let oldDescription = resource.State.Description

            let updatedResourceData = {
                resource.State with
                    Description = validDescription
                    UpdatedAt = DateTime.UtcNow
            }

            let descriptionChangedEvent =
                ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                    ResourceChange.DescriptionChanged(oldDescription, validDescription)
                ]

            Ok {
                State = updatedResourceData
                UncommittedEvents = resource.UncommittedEvents @ [ descriptionChangedEvent :> IDomainEvent ]
            }

    let updateBaseUrl
        (actorUserId: UserId)
        (newBaseUrl: string)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match resource.State.ResourceKind with
        | ResourceKind.Sql -> Error(InvalidOperation "Base URL can only be updated for HTTP resources")
        | ResourceKind.Http ->
            match BaseUrl.Create(Some newBaseUrl) with
            | Error err -> Error err
            | Ok validBaseUrl ->
                match resource.State.BaseUrl with
                | None -> Error(InvalidOperation "HTTP resource is missing a base URL")
                | Some oldBaseUrl ->
                    let updatedResourceData = {
                        resource.State with
                            BaseUrl = Some validBaseUrl
                            UpdatedAt = DateTime.UtcNow
                    }

                    let baseUrlChangedEvent =
                        ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                            ResourceChange.BaseUrlChanged(oldBaseUrl, validBaseUrl)
                        ]

                    Ok {
                        State = updatedResourceData
                        UncommittedEvents = resource.UncommittedEvents @ [ baseUrlChangedEvent :> IDomainEvent ]
                    }

    let private checkAppConflicts
        (apps: AppResourceConflictData list)
        (urlParameters: (string * string) list option)
        (headers: (string * string) list option)
        (body: (string * string) list option)
        : Result<unit, DomainError> =
        BusinessRules.checkAppToResourceConflicts apps urlParameters headers body

    let updateUrlParameters
        (actorUserId: UserId)
        (newUrlParameters: (string * string) list)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match resource.State.ResourceKind with
        | ResourceKind.Sql -> Error(InvalidOperation "URL parameters can only be updated for HTTP resources")
        | ResourceKind.Http ->
            let validateKeyValuePairs pairs =
                pairs
                |> List.fold
                    (fun acc (key: string, value: string) ->
                        match acc with
                        | Error err -> Error err
                        | Ok validPairs ->
                            match KeyValuePair.Create(key, value) with
                            | Error err -> Error err
                            | Ok validPair -> Ok(validPair :: validPairs))
                    (Ok [])
                |> Result.map List.rev

            match validateKeyValuePairs newUrlParameters with
            | Error err -> Error err
            | Ok validUrlParams ->
                match checkAppConflicts apps (Some newUrlParameters) None None with
                | Error err -> Error err
                | Ok() ->
                    let oldUrlParams = resource.State.UrlParameters

                    let updatedResourceData = {
                        resource.State with
                            UrlParameters = validUrlParams
                            UpdatedAt = DateTime.UtcNow
                    }

                    let urlParamsChangedEvent =
                        ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                            ResourceChange.UrlParametersChanged(oldUrlParams, validUrlParams)
                        ]

                    Ok {
                        State = updatedResourceData
                        UncommittedEvents = resource.UncommittedEvents @ [ urlParamsChangedEvent :> IDomainEvent ]
                    }

    let updateHeaders
        (actorUserId: UserId)
        (newHeaders: (string * string) list)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match resource.State.ResourceKind with
        | ResourceKind.Sql -> Error(InvalidOperation "Headers can only be updated for HTTP resources")
        | ResourceKind.Http ->
            let validateKeyValuePairs pairs =
                pairs
                |> List.fold
                    (fun acc (key: string, value: string) ->
                        match acc with
                        | Error err -> Error err
                        | Ok validPairs ->
                            match KeyValuePair.Create(key, value) with
                            | Error err -> Error err
                            | Ok validPair -> Ok(validPair :: validPairs))
                    (Ok [])
                |> Result.map List.rev

            match validateKeyValuePairs newHeaders with
            | Error err -> Error err
            | Ok validHeaders ->
                match checkAppConflicts apps None (Some newHeaders) None with
                | Error err -> Error err
                | Ok() ->
                    let oldHeaders = resource.State.Headers

                    let updatedResourceData = {
                        resource.State with
                            Headers = validHeaders
                            UpdatedAt = DateTime.UtcNow
                    }

                    let headersChangedEvent =
                        ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                            ResourceChange.HeadersChanged(oldHeaders, validHeaders)
                        ]

                    Ok {
                        State = updatedResourceData
                        UncommittedEvents = resource.UncommittedEvents @ [ headersChangedEvent :> IDomainEvent ]
                    }

    let updateBody
        (actorUserId: UserId)
        (newBody: (string * string) list)
        (apps: AppResourceConflictData list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match resource.State.ResourceKind with
        | ResourceKind.Sql -> Error(InvalidOperation "Body can only be updated for HTTP resources")
        | ResourceKind.Http ->
            let validateKeyValuePairs pairs =
                pairs
                |> List.fold
                    (fun acc (key: string, value: string) ->
                        match acc with
                        | Error err -> Error err
                        | Ok validPairs ->
                            match KeyValuePair.Create(key, value) with
                            | Error err -> Error err
                            | Ok validPair -> Ok(validPair :: validPairs))
                    (Ok [])
                |> Result.map List.rev

            match validateKeyValuePairs newBody with
            | Error err -> Error err
            | Ok validBody ->
                match checkAppConflicts apps None None (Some newBody) with
                | Error err -> Error err
                | Ok() ->
                    let oldBody = resource.State.Body

                    let updatedResourceData = {
                        resource.State with
                            Body = validBody
                            UpdatedAt = DateTime.UtcNow
                    }

                    let bodyChangedEvent =
                        ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                            ResourceChange.BodyChanged(oldBody, validBody)
                        ]

                    Ok {
                        State = updatedResourceData
                        UncommittedEvents = resource.UncommittedEvents @ [ bodyChangedEvent :> IDomainEvent ]
                    }

    let private toDatabaseConfigSummary (resourceData: ResourceData) : Result<DatabaseConfigSummary, DomainError> =
        match
            resourceData.DatabaseName,
            resourceData.DatabaseHost,
            resourceData.DatabasePort,
            resourceData.DatabaseEngine,
            resourceData.DatabaseAuthScheme,
            resourceData.DatabaseUsername
        with
        | Some databaseName, Some databaseHost, Some databasePort, Some databaseEngine, Some authScheme, Some username ->
            Ok {
                DatabaseName = databaseName
                Host = databaseHost
                Port = databasePort
                Engine = databaseEngine
                AuthScheme = authScheme
                Username = username
                UseSsl = resourceData.UseSsl
                EnableSshTunnel = resourceData.EnableSshTunnel
                ConnectionOptions = resourceData.ConnectionOptions
                HasPassword = resourceData.DatabasePassword.IsSome
            }
        | _ -> Error(InvalidOperation "SQL resource is missing database configuration")

    let updateDatabaseConfig
        (actorUserId: UserId)
        (databaseName: string)
        (databaseHost: string)
        (databasePort: int)
        (databaseEngine: string)
        (databaseAuthScheme: string)
        (databaseUsername: string)
        (databasePassword: string option)
        (useSsl: bool)
        (enableSshTunnel: bool)
        (connectionOptions: (string * string) list)
        (resource: ValidatedResource)
        : Result<ValidatedResource, DomainError> =
        match resource.State.ResourceKind with
        | ResourceKind.Http -> Error(InvalidOperation "Database configuration can only be updated for SQL resources")
        | ResourceKind.Sql ->
            match DatabaseName.Create(Some databaseName) with
            | Error err -> Error err
            | Ok validDatabaseName ->
                match DatabaseHost.Create(Some databaseHost) with
                | Error err -> Error err
                | Ok validDatabaseHost ->
                    match DatabasePort.Create(Some databasePort) with
                    | Error err -> Error err
                    | Ok validDatabasePort ->
                        match DatabaseEngine.Create(databaseEngine) with
                        | Error err -> Error err
                        | Ok validEngine ->
                            match DatabaseAuthScheme.Create(databaseAuthScheme) with
                            | Error err -> Error err
                            | Ok validAuthScheme ->
                                match DatabaseUsername.Create(Some databaseUsername) with
                                | Error err -> Error err
                                | Ok validUsername ->
                                    let validatedPasswordResult =
                                        match databasePassword with
                                        | Some rawPassword ->
                                            DatabasePassword.Create(Some rawPassword) |> Result.map Some
                                        | None ->
                                            match resource.State.DatabasePassword with
                                            | Some existingPassword -> Ok(Some existingPassword)
                                            | None -> Error(ValidationError "Database password is required")

                                    match validatedPasswordResult with
                                    | Error err -> Error err
                                    | Ok validPassword ->
                                        let validateKeyValuePairs pairs =
                                            pairs
                                            |> List.fold
                                                (fun acc (key: string, value: string) ->
                                                    match acc with
                                                    | Error err -> Error err
                                                    | Ok validPairs ->
                                                        match KeyValuePair.Create(key, value) with
                                                        | Error err -> Error err
                                                        | Ok validPair -> Ok(validPair :: validPairs))
                                                (Ok [])
                                            |> Result.map List.rev

                                        match validateKeyValuePairs connectionOptions with
                                        | Error err -> Error err
                                        | Ok validOptions ->
                                            match toDatabaseConfigSummary resource.State with
                                            | Error err -> Error err
                                            | Ok oldSummary ->
                                                let newSummary = {
                                                    DatabaseName = validDatabaseName
                                                    Host = validDatabaseHost
                                                    Port = validDatabasePort
                                                    Engine = validEngine
                                                    AuthScheme = validAuthScheme
                                                    Username = validUsername
                                                    UseSsl = useSsl
                                                    EnableSshTunnel = enableSshTunnel
                                                    ConnectionOptions = validOptions
                                                    HasPassword = validPassword.IsSome
                                                }

                                                let updatedResourceData = {
                                                    resource.State with
                                                        DatabaseName = Some validDatabaseName
                                                        DatabaseHost = Some validDatabaseHost
                                                        DatabasePort = Some validDatabasePort
                                                        DatabaseEngine = Some validEngine
                                                        DatabaseAuthScheme = Some validAuthScheme
                                                        DatabaseUsername = Some validUsername
                                                        DatabasePassword = validPassword
                                                        UseSsl = useSsl
                                                        EnableSshTunnel = enableSshTunnel
                                                        ConnectionOptions = validOptions
                                                        UpdatedAt = DateTime.UtcNow
                                                }

                                                let configChangedEvent =
                                                    ResourceEvents.resourceUpdated actorUserId resource.State.Id [
                                                        ResourceChange.DatabaseConfigChanged(oldSummary, newSummary)
                                                    ]

                                                Ok {
                                                    State = updatedResourceData
                                                    UncommittedEvents =
                                                        resource.UncommittedEvents
                                                        @ [ configChangedEvent :> IDomainEvent ]
                                                }

    let markForDeletion (actorUserId: UserId) (resource: ValidatedResource) : ValidatedResource =
        let resourceDeletedEvent =
            ResourceEvents.resourceDeleted actorUserId resource.State.Id resource.State.Name

        {
            resource with
                UncommittedEvents = resource.UncommittedEvents @ [ resourceDeletedEvent :> IDomainEvent ]
        }

    let restore (actorUserId: UserId) (newName: ResourceName option) (resource: ValidatedResource) : ValidatedResource =
        let finalName = newName |> Option.defaultValue resource.State.Name

        let resourceRestoredEvent =
            ResourceEvents.resourceRestored actorUserId resource.State.Id finalName

        {
            resource with
                State = {
                    resource.State with
                        Name = finalName
                        IsDeleted = false
                        UpdatedAt = DateTime.UtcNow
                }
                UncommittedEvents = resource.UncommittedEvents @ [ resourceRestoredEvent :> IDomainEvent ]
        }

    let getUncommittedEvents (resource: ValidatedResource) : IDomainEvent list = resource.UncommittedEvents

    let markEventsAsCommitted (resource: ValidatedResource) : ValidatedResource = {
        resource with
            UncommittedEvents = []
    }

    let getId (resource: Resource) : ResourceId = resource.State.Id

    let getName (resource: Resource) : string = resource.State.Name.Value

    let getDescription (resource: Resource) : string = resource.State.Description.Value

    let getSpaceId (resource: Resource) : SpaceId = resource.State.SpaceId

    let getResourceKind (resource: Resource) : ResourceKind = resource.State.ResourceKind

    let getBaseUrl (resource: Resource) : string option =
        resource.State.BaseUrl |> Option.map (fun baseUrl -> baseUrl.Value)

    let getUrlParameters (resource: Resource) : (string * string) list =
        resource.State.UrlParameters |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getHeaders (resource: Resource) : (string * string) list =
        resource.State.Headers |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getBody (resource: Resource) : (string * string) list =
        resource.State.Body |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getCreatedAt (resource: Resource) : DateTime = resource.State.CreatedAt

    let getUpdatedAt (resource: Resource) : DateTime = resource.State.UpdatedAt

    let toConflictData (resource: Resource) : ResourceAppConflictData = {
        UrlParameters = getUrlParameters resource
        Headers = getHeaders resource
        Body = getBody resource
    }