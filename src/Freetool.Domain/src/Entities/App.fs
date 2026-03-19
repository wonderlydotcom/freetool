namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

[<Table("Apps")>]
[<Index([| "Name"; "FolderId" |], IsUnique = true, Name = "IX_Apps_Name_FolderId")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type AppData = {
    [<Key>]
    Id: AppId

    [<Required>]
    [<MaxLength(100)>]
    Name: string

    [<Required>]
    FolderId: FolderId

    [<Required>]
    ResourceId: ResourceId

    [<Required>]
    [<MaxLength(10)>]
    HttpMethod: HttpMethod

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON serialized list of inputs
    Inputs: Input list

    [<MaxLength(500)>]
    UrlPath: string option

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON serialized key-value pairs
    UrlParameters: KeyValuePair list

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON serialized key-value pairs
    Headers: KeyValuePair list

    [<Required>]
    [<Column(TypeName = "TEXT")>] // JSON serialized key-value pairs
    Body: KeyValuePair list

    [<Required>]
    UseDynamicJsonBody: bool

    [<Column(TypeName = "TEXT")>] // JSON serialized SQL config (null for HTTP apps)
    SqlConfig: SqlQueryConfig option

    [<MaxLength(500)>]
    Description: string option

    [<Required>]
    CreatedAt: DateTime

    [<Required>]
    UpdatedAt: DateTime

    [<JsonIgnore>]
    IsDeleted: bool
}

type App = EventSourcingAggregate<AppData>

module AppAggregateHelpers =
    let getEntityId (app: App) : AppId = app.State.Id

    let implementsIEntity (app: App) =
        { new IEntity<AppId> with
            member _.Id = app.State.Id
        }

// Type aliases for clarity
type UnvalidatedApp = App // From DTOs - potentially unsafe
type ValidatedApp = App // Validated domain model and database data

module App =
    // Validate input
    let private validateInput (input: Input) : Result<Input, DomainError> =
        match InputTitle.Create(Some input.Title) with
        | Error err -> Error err
        | Ok validTitle ->
            match input.Type.Value with
            | InputTypeValue.Boolean when not input.Required -> Error(ValidationError "Boolean inputs must be required")
            | _ ->
                match input.Description with
                | Some desc when desc.Length > 100 ->
                    Error(ValidationError "Input description cannot exceed 100 characters")
                | _ -> Ok { input with Title = validTitle.Value }

    let private normalizeSqlText (value: string option) : string option =
        value
        |> Option.map (fun v -> v.Trim())
        |> Option.bind (fun v -> if v = "" then None else Some v)

    let private validateSqlConfig (sqlConfig: SqlQueryConfig option) : Result<SqlQueryConfig option, DomainError> =
        match sqlConfig with
        | None -> Ok None
        | Some config ->
            let normalizedColumns =
                config.Columns |> List.map (fun c -> c.Trim()) |> List.filter (fun c -> c <> "")

            let normalizedOrderBy =
                config.OrderBy
                |> List.map (fun order -> {
                    order with
                        Column = order.Column.Trim()
                })
                |> List.filter (fun order -> order.Column <> "")

            let validateFilter (filter: SqlFilter) =
                if System.String.IsNullOrWhiteSpace(filter.Column) then
                    Error(ValidationError "SQL filter column is required")
                else
                    match filter.Operator with
                    | SqlFilterOperator.IsNull
                    | SqlFilterOperator.IsNotNull ->
                        if filter.Value.IsSome then
                            Error(ValidationError "SQL filter value must be empty for IS NULL operators")
                        else
                            Ok {
                                filter with
                                    Column = filter.Column.Trim()
                                    Value = None
                            }
                    | _ ->
                        match normalizeSqlText filter.Value with
                        | None -> Error(ValidationError "SQL filter value is required")
                        | Some value ->
                            Ok {
                                filter with
                                    Column = filter.Column.Trim()
                                    Value = Some value
                            }

            let validateFilters =
                config.Filters
                |> List.map validateFilter
                |> List.fold
                    (fun acc item ->
                        match acc, item with
                        | Error err, _ -> Error err
                        | _, Error err -> Error err
                        | Ok items, Ok valid -> Ok(valid :: items))
                    (Ok [])
                |> Result.map List.rev

            let normalizedLimit =
                match config.Limit with
                | Some limit when limit <= 0 -> Error(ValidationError "SQL limit must be greater than zero")
                | _ -> Ok config.Limit

            match config.Mode with
            | SqlQueryMode.Gui ->
                match normalizeSqlText config.Table with
                | None -> Error(ValidationError "SQL table is required for GUI mode")
                | Some table ->
                    match validateFilters, normalizedLimit with
                    | Error err, _ -> Error err
                    | _, Error err -> Error err
                    | Ok validFilters, Ok validLimit ->
                        Ok(
                            Some {
                                config with
                                    Table = Some table
                                    Columns = normalizedColumns
                                    Filters = validFilters
                                    Limit = validLimit
                                    OrderBy = normalizedOrderBy
                                    RawSql = None
                            }
                        )
            | SqlQueryMode.Raw ->
                match normalizeSqlText config.RawSql with
                | None -> Error(ValidationError "Raw SQL is required for SQL raw mode")
                | Some rawSql ->
                    Ok(
                        Some {
                            config with
                                Table = None
                                Columns = []
                                Filters = []
                                Limit = None
                                OrderBy = []
                                RawSql = Some rawSql
                        }
                    )

    let fromData (appData: AppData) : ValidatedApp = {
        State = appData
        UncommittedEvents = []
    }

    let validate (app: UnvalidatedApp) : Result<ValidatedApp, DomainError> =
        let appData = app.State

        match AppName.Create(Some appData.Name) with
        | Error err -> Error err
        | Ok validName ->
            // Validate all inputs
            let validateInputs inputs =
                let rec validateList acc remaining =
                    match remaining with
                    | [] -> Ok(List.rev acc)
                    | input :: rest ->
                        match validateInput input with
                        | Error err -> Error err
                        | Ok validInput -> validateList (validInput :: acc) rest

                validateList [] inputs

            match validateInputs appData.Inputs with
            | Error err -> Error err
            | Ok validInputs ->
                Ok {
                    State = {
                        appData with
                            Name = validName.Value
                            Inputs = validInputs
                    }
                    UncommittedEvents = app.UncommittedEvents
                }

    let updateName (actorUserId: UserId) (newName: string) (app: ValidatedApp) : Result<ValidatedApp, DomainError> =
        match AppName.Create(Some newName) with
        | Error err -> Error err
        | Ok validName ->
            let oldName =
                AppName.Create(Some app.State.Name) |> Result.defaultValue (AppName(""))

            let updatedAppData = {
                app.State with
                    Name = validName.Value
                    UpdatedAt = DateTime.UtcNow
            }

            let nameChangedEvent =
                AppEvents.appUpdated actorUserId app.State.Id [ AppChange.NameChanged(oldName, validName) ]

            Ok {
                State = updatedAppData
                UncommittedEvents = app.UncommittedEvents @ [ nameChangedEvent :> IDomainEvent ]
            }

    let updateInputs
        (actorUserId: UserId)
        (newInputs: Input list)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        let validateInputs inputs =
            let rec validateList acc remaining =
                match remaining with
                | [] -> Ok(List.rev acc)
                | input :: rest ->
                    match validateInput input with
                    | Error err -> Error err
                    | Ok validInput -> validateList (validInput :: acc) rest

            validateList [] inputs

        match validateInputs newInputs with
        | Error err -> Error err
        | Ok validInputs ->
            let oldInputs = app.State.Inputs

            let updatedAppData = {
                app.State with
                    Inputs = validInputs
                    UpdatedAt = DateTime.UtcNow
            }

            let inputsChangedEvent =
                AppEvents.appUpdated actorUserId app.State.Id [ AppChange.InputsChanged(oldInputs, validInputs) ]

            Ok {
                State = updatedAppData
                UncommittedEvents = app.UncommittedEvents @ [ inputsChangedEvent :> IDomainEvent ]
            }

    let private checkResourceConflicts
        (resource: ResourceAppConflictData)
        (urlParameters: (string * string) list option)
        (headers: (string * string) list option)
        (body: (string * string) list option)
        : Result<unit, DomainError> =
        BusinessRules.checkResourceToAppConflicts resource urlParameters headers body

    let private createInternal
        (actorUserId: UserId)
        (name: string)
        (folderId: FolderId)
        (resourceId: ResourceId)
        (httpMethod: HttpMethod)
        (inputs: Input list)
        (urlPath: string option)
        (urlParameters: (string * string) list)
        (headers: (string * string) list)
        (body: (string * string) list)
        (useDynamicJsonBody: bool)
        (sqlConfig: SqlQueryConfig option)
        (description: string option)
        : Result<ValidatedApp, DomainError> =
        // Validate headers
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

        match validateKeyValuePairs urlParameters with
        | Error err -> Error err
        | Ok validUrlParameters ->
            match validateKeyValuePairs headers with
            | Error err -> Error err
            | Ok validHeaders ->
                match validateKeyValuePairs body with
                | Error err -> Error err
                | Ok validBody ->
                    match validateSqlConfig sqlConfig with
                    | Error err -> Error err
                    | Ok validSqlConfig ->
                        let appData = {
                            Id = AppId.NewId()
                            Name = name
                            FolderId = folderId
                            ResourceId = resourceId
                            HttpMethod = httpMethod
                            Inputs = inputs
                            UrlPath = urlPath
                            UrlParameters = validUrlParameters
                            Headers = validHeaders
                            Body = validBody
                            UseDynamicJsonBody = useDynamicJsonBody
                            SqlConfig = validSqlConfig
                            Description = description
                            CreatedAt = DateTime.UtcNow
                            UpdatedAt = DateTime.UtcNow
                            IsDeleted = false
                        }

                        let unvalidatedApp = {
                            State = appData
                            UncommittedEvents = []
                        }

                        match validate unvalidatedApp with
                        | Error err -> Error err
                        | Ok validatedApp ->
                            let validName = AppName.Create(Some name) |> Result.defaultValue (AppName(""))

                            let appCreatedEvent =
                                AppEvents.appCreated
                                    actorUserId
                                    appData.Id
                                    validName
                                    (Some folderId)
                                    resourceId
                                    httpMethod
                                    inputs

                            Ok {
                                validatedApp with
                                    UncommittedEvents = [ appCreatedEvent :> IDomainEvent ]
                            }

    let createWithSqlConfig
        (actorUserId: UserId)
        (name: string)
        (folderId: FolderId)
        (resource: ValidatedResource)
        (httpMethod: HttpMethod)
        (inputs: Input list)
        (urlPath: string option)
        (urlParameters: (string * string) list)
        (headers: (string * string) list)
        (body: (string * string) list)
        (useDynamicJsonBody: bool)
        (sqlConfig: SqlQueryConfig option)
        (description: string option)
        : Result<ValidatedApp, DomainError> =
        match Resource.getResourceKind resource with
        | ResourceKind.Http ->
            // Business rule: App cannot override existing Resource parameters
            let resourceConflictData = Resource.toConflictData resource

            match checkResourceConflicts resourceConflictData (Some urlParameters) (Some headers) (Some body) with
            | Error err -> Error err
            | Ok() ->
                if sqlConfig.IsSome then
                    Error(ValidationError "SQL config is only allowed for SQL resources")
                else
                    // No conflicts, proceed with normal creation
                    let resourceId = Resource.getId resource

                    createInternal
                        actorUserId
                        name
                        folderId
                        resourceId
                        httpMethod
                        inputs
                        urlPath
                        urlParameters
                        headers
                        body
                        useDynamicJsonBody
                        None
                        description
        | ResourceKind.Sql ->
            if sqlConfig.IsNone then
                Error(ValidationError "SQL config is required for SQL resources")
            elif useDynamicJsonBody then
                Error(ValidationError "Dynamic JSON body is not supported for SQL resources")
            elif
                (urlPath |> Option.exists (fun p -> p.Trim() <> ""))
                || not urlParameters.IsEmpty
                || not headers.IsEmpty
                || not body.IsEmpty
            then
                Error(ValidationError "HTTP fields are not allowed for SQL resources")
            else
                let resourceId = Resource.getId resource

                createInternal
                    actorUserId
                    name
                    folderId
                    resourceId
                    httpMethod
                    inputs
                    None
                    []
                    []
                    []
                    false
                    sqlConfig
                    description

    let create
        (actorUserId: UserId)
        (name: string)
        (folderId: FolderId)
        (resource: ValidatedResource)
        (httpMethod: HttpMethod)
        (inputs: Input list)
        (urlPath: string option)
        (urlParameters: (string * string) list)
        (headers: (string * string) list)
        (body: (string * string) list)
        (useDynamicJsonBody: bool)
        (description: string option)
        : Result<ValidatedApp, DomainError> =
        createWithSqlConfig
            actorUserId
            name
            folderId
            resource
            httpMethod
            inputs
            urlPath
            urlParameters
            headers
            body
            useDynamicJsonBody
            None
            description



    let markForDeletion (actorUserId: UserId) (app: ValidatedApp) : ValidatedApp =
        let appName =
            AppName.Create(Some app.State.Name) |> Result.defaultValue (AppName(""))

        let appDeletedEvent = AppEvents.appDeleted actorUserId app.State.Id appName

        {
            app with
                UncommittedEvents = app.UncommittedEvents @ [ appDeletedEvent :> IDomainEvent ]
        }

    let restore (actorUserId: UserId) (newName: string option) (app: ValidatedApp) : ValidatedApp =
        let finalName = newName |> Option.defaultValue app.State.Name
        let appName = AppName.Create(Some finalName) |> Result.defaultValue (AppName(""))
        let appRestoredEvent = AppEvents.appRestored actorUserId app.State.Id appName

        {
            app with
                State = {
                    app.State with
                        Name = finalName
                        IsDeleted = false
                        UpdatedAt = DateTime.UtcNow
                }
                UncommittedEvents = app.UncommittedEvents @ [ appRestoredEvent :> IDomainEvent ]
        }

    let getUncommittedEvents (app: ValidatedApp) : IDomainEvent list = app.UncommittedEvents

    let markEventsAsCommitted (app: ValidatedApp) : ValidatedApp = { app with UncommittedEvents = [] }

    let getId (app: App) : AppId = app.State.Id

    let getName (app: App) : string = app.State.Name

    let getFolderId (app: App) : FolderId = app.State.FolderId

    let getResourceId (app: App) : ResourceId = app.State.ResourceId

    let getHttpMethod (app: App) : string = app.State.HttpMethod.ToString()

    let getInputs (app: App) : Input list = app.State.Inputs

    let getCreatedAt (app: App) : DateTime = app.State.CreatedAt

    let getUpdatedAt (app: App) : DateTime = app.State.UpdatedAt

    let getUrlPath (app: App) : string option = app.State.UrlPath

    let getUrlParameters (app: App) : (string * string) list =
        app.State.UrlParameters |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getHeaders (app: App) : (string * string) list =
        app.State.Headers |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getBody (app: App) : (string * string) list =
        app.State.Body |> List.map (fun kvp -> (kvp.Key, kvp.Value))

    let getUseDynamicJsonBody (app: App) : bool = app.State.UseDynamicJsonBody

    let getSqlConfig (app: App) : SqlQueryConfig option = app.State.SqlConfig

    let getDescription (app: App) : string option = app.State.Description

    let toConflictData (app: App) : AppResourceConflictData = {
        AppId = (getId app).ToString()
        UrlParameters = getUrlParameters app
        Headers = getHeaders app
        Body = getBody app
    }

    let updateUrlPath
        (actorUserId: UserId)
        (newUrlPath: string option)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        let updatedAppData = {
            app.State with
                UrlPath = newUrlPath
                UpdatedAt = DateTime.UtcNow
        }

        let urlPathChangedEvent =
            AppEvents.appUpdated actorUserId app.State.Id [ AppChange.UrlPathChanged(app.State.UrlPath, newUrlPath) ]

        Ok {
            State = updatedAppData
            UncommittedEvents = app.UncommittedEvents @ [ urlPathChangedEvent :> IDomainEvent ]
        }

    let updateUrlParameters
        (actorUserId: UserId)
        (newUrlParameters: (string * string) list)
        (resource: ResourceAppConflictData)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
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
            match checkResourceConflicts resource (Some newUrlParameters) None None with
            | Error err -> Error err
            | Ok() ->
                let oldUrlParams = app.State.UrlParameters

                let updatedAppData = {
                    app.State with
                        UrlParameters = validUrlParams
                        UpdatedAt = DateTime.UtcNow
                }

                let urlParamsChangedEvent =
                    AppEvents.appUpdated actorUserId app.State.Id [
                        AppChange.UrlParametersChanged(oldUrlParams, validUrlParams)
                    ]

                Ok {
                    State = updatedAppData
                    UncommittedEvents = app.UncommittedEvents @ [ urlParamsChangedEvent :> IDomainEvent ]
                }

    let updateHeaders
        (actorUserId: UserId)
        (newHeaders: (string * string) list)
        (resource: ResourceAppConflictData)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
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
            match checkResourceConflicts resource None (Some newHeaders) None with
            | Error err -> Error err
            | Ok() ->
                let oldHeaders = app.State.Headers

                let updatedAppData = {
                    app.State with
                        Headers = validHeaders
                        UpdatedAt = DateTime.UtcNow
                }

                let headersChangedEvent =
                    AppEvents.appUpdated actorUserId app.State.Id [ AppChange.HeadersChanged(oldHeaders, validHeaders) ]

                Ok {
                    State = updatedAppData
                    UncommittedEvents = app.UncommittedEvents @ [ headersChangedEvent :> IDomainEvent ]
                }

    let updateBody
        (actorUserId: UserId)
        (newBody: (string * string) list)
        (resource: ResourceAppConflictData)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
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
            match checkResourceConflicts resource None None (Some newBody) with
            | Error err -> Error err
            | Ok() ->
                let oldBody = app.State.Body

                let updatedAppData = {
                    app.State with
                        Body = validBody
                        UpdatedAt = DateTime.UtcNow
                }

                let bodyChangedEvent =
                    AppEvents.appUpdated actorUserId app.State.Id [ AppChange.BodyChanged(oldBody, validBody) ]

                Ok {
                    State = updatedAppData
                    UncommittedEvents = app.UncommittedEvents @ [ bodyChangedEvent :> IDomainEvent ]
                }

    let updateHttpMethod
        (actorUserId: UserId)
        (newHttpMethod: string)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        match HttpMethod.Create(newHttpMethod) with
        | Error err -> Error err
        | Ok validHttpMethod ->
            let oldHttpMethod = app.State.HttpMethod

            let updatedAppData = {
                app.State with
                    HttpMethod = validHttpMethod
                    UpdatedAt = DateTime.UtcNow
            }

            let httpMethodChangedEvent =
                AppEvents.appUpdated actorUserId app.State.Id [
                    AppChange.HttpMethodChanged(oldHttpMethod, validHttpMethod)
                ]

            Ok {
                State = updatedAppData
                UncommittedEvents = app.UncommittedEvents @ [ httpMethodChangedEvent :> IDomainEvent ]
            }

    let updateUseDynamicJsonBody
        (actorUserId: UserId)
        (newUseDynamicJsonBody: bool)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        let oldUseDynamicJsonBody = app.State.UseDynamicJsonBody

        let updatedAppData = {
            app.State with
                UseDynamicJsonBody = newUseDynamicJsonBody
                UpdatedAt = DateTime.UtcNow
        }

        let useDynamicJsonBodyChangedEvent =
            AppEvents.appUpdated actorUserId app.State.Id [
                AppChange.UseDynamicJsonBodyChanged(oldUseDynamicJsonBody, newUseDynamicJsonBody)
            ]

        Ok {
            State = updatedAppData
            UncommittedEvents = app.UncommittedEvents @ [ useDynamicJsonBodyChangedEvent :> IDomainEvent ]
        }

    let updateSqlConfig
        (actorUserId: UserId)
        (newSqlConfig: SqlQueryConfig option)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        match validateSqlConfig newSqlConfig with
        | Error err -> Error err
        | Ok validSqlConfig ->
            let updatedAppData = {
                app.State with
                    SqlConfig = validSqlConfig
                    UpdatedAt = DateTime.UtcNow
            }

            let sqlConfigChangedEvent =
                AppEvents.appUpdated actorUserId app.State.Id [
                    AppChange.SqlConfigChanged(app.State.SqlConfig, validSqlConfig)
                ]

            Ok {
                State = updatedAppData
                UncommittedEvents = app.UncommittedEvents @ [ sqlConfigChangedEvent :> IDomainEvent ]
            }

    let updateDescription
        (actorUserId: UserId)
        (newDescription: string option)
        (app: ValidatedApp)
        : Result<ValidatedApp, DomainError> =
        // Validate description length
        match newDescription with
        | Some desc when desc.Length > 500 -> Error(ValidationError "Description cannot exceed 500 characters")
        | _ ->
            let oldDescription = app.State.Description

            let updatedAppData = {
                app.State with
                    Description = newDescription
                    UpdatedAt = DateTime.UtcNow
            }

            let descriptionChangedEvent =
                AppEvents.appUpdated actorUserId app.State.Id [
                    AppChange.DescriptionChanged(oldDescription, newDescription)
                ]

            Ok {
                State = updatedAppData
                UncommittedEvents = app.UncommittedEvents @ [ descriptionChangedEvent :> IDomainEvent ]
            }