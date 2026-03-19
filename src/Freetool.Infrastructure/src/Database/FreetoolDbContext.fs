namespace Freetool.Infrastructure.Database

open System
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage.ValueConversion
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects

// Helper module for ExecutableHttpRequest serialization
// Using CLIMutable records for JSON serialization compatibility
[<CLIMutable>]
type KeyValuePair = { Key: string; Value: string }

[<CLIMutable>]
type SerializableHttpRequest = {
    BaseUrl: string
    UrlParameters: KeyValuePair array
    Headers: KeyValuePair array
    Body: KeyValuePair array
    HttpMethod: string
    UseJsonBody: bool
}

[<CLIMutable>]
type IdentityGroupSpaceMappingData = {
    Id: Guid
    GroupKey: string
    SpaceId: SpaceId
    IsActive: bool
    CreatedByUserId: UserId
    UpdatedByUserId: UserId
    CreatedAt: DateTime
    UpdatedAt: DateTime
}

module ExecutableHttpRequestSerializer =
    let serialize (requestOpt: Freetool.Domain.ExecutableHttpRequest option) : string =
        match requestOpt with
        | None -> null
        | Some request ->
            let serializable: SerializableHttpRequest = {
                BaseUrl = request.BaseUrl
                UrlParameters =
                    request.UrlParameters
                    |> List.map (fun (k, v) -> { Key = k; Value = v })
                    |> List.toArray
                Headers =
                    request.Headers
                    |> List.map (fun (k, v) -> { Key = k; Value = v })
                    |> List.toArray
                Body = request.Body |> List.map (fun (k, v) -> { Key = k; Value = v }) |> List.toArray
                HttpMethod = request.HttpMethod
                UseJsonBody = request.UseJsonBody
            }

            System.Text.Json.JsonSerializer.Serialize(serializable)

    let deserialize (json: string) : Freetool.Domain.ExecutableHttpRequest option =
        if System.String.IsNullOrEmpty(json) then
            None
        else
            try
                let deserialized =
                    System.Text.Json.JsonSerializer.Deserialize<SerializableHttpRequest>(json)

                Some {
                    Freetool.Domain.ExecutableHttpRequest.BaseUrl = deserialized.BaseUrl
                    UrlParameters =
                        deserialized.UrlParameters
                        |> Array.toList
                        |> List.map (fun kv -> (kv.Key, kv.Value))
                    Headers = deserialized.Headers |> Array.toList |> List.map (fun kv -> (kv.Key, kv.Value))
                    Body = deserialized.Body |> Array.toList |> List.map (fun kv -> (kv.Key, kv.Value))
                    HttpMethod = deserialized.HttpMethod
                    UseJsonBody = deserialized.UseJsonBody
                }
            with _ ->
                None

type FreetoolDbContext(options: DbContextOptions<FreetoolDbContext>) =
    inherit DbContext(options)

    [<DefaultValue>]
    val mutable private _users: DbSet<UserData>

    [<DefaultValue>]
    val mutable private _resources: DbSet<ResourceData>

    [<DefaultValue>]
    val mutable private _folders: DbSet<FolderData>

    [<DefaultValue>]
    val mutable private _apps: DbSet<AppData>

    [<DefaultValue>]
    val mutable private _dashboards: DbSet<DashboardData>

    [<DefaultValue>]
    val mutable private _events: DbSet<EventData>

    [<DefaultValue>]
    val mutable private _runs: DbSet<RunData>

    [<DefaultValue>]
    val mutable private _spaces: DbSet<SpaceData>

    [<DefaultValue>]
    val mutable private _spaceMembers: DbSet<SpaceMemberData>

    [<DefaultValue>]
    val mutable private _identityGroupSpaceMappings: DbSet<IdentityGroupSpaceMappingData>

    member this.Users
        with get () = this._users
        and set value = this._users <- value

    member this.Resources
        with get () = this._resources
        and set value = this._resources <- value

    member this.Folders
        with get () = this._folders
        and set value = this._folders <- value

    member this.Apps
        with get () = this._apps
        and set value = this._apps <- value

    member this.Dashboards
        with get () = this._dashboards
        and set value = this._dashboards <- value

    member this.Events
        with get () = this._events
        and set value = this._events <- value

    member this.Runs
        with get () = this._runs
        and set value = this._runs <- value

    member this.Spaces
        with get () = this._spaces
        and set value = this._spaces <- value

    member this.SpaceMembers
        with get () = this._spaceMembers
        and set value = this._spaceMembers <- value

    member this.IdentityGroupSpaceMappings
        with get () = this._identityGroupSpaceMappings
        and set value = this._identityGroupSpaceMappings <- value

    override this.OnModelCreating(modelBuilder: ModelBuilder) =
        base.OnModelCreating modelBuilder

        // Ignore value types that shouldn't be treated as entities
        modelBuilder.Ignore<Freetool.Domain.RunInputValue>() |> ignore
        modelBuilder.Ignore<Freetool.Domain.Events.Input>() |> ignore
        modelBuilder.Ignore<Freetool.Domain.ExecutableHttpRequest>() |> ignore
        modelBuilder.Ignore<string option>() |> ignore

        // Set up value converters for custom types and complex objects
        let optionStringConverter =
            ValueConverter<string option, string>(
                (fun opt ->
                    match opt with
                    | Some s -> s
                    | None -> null),
                (fun str -> if isNull str then None else Some str)
            )

        // Configure UserData
        modelBuilder.Entity<UserData>(fun entity ->
            let userIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.UserId, System.Guid>(
                    (fun userId -> userId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.UserId(guid))
                )

            let optionDateTimeConverter =
                ValueConverter<DateTime option, System.Nullable<DateTime>>(
                    (fun opt ->
                        match opt with
                        | Some dt -> System.Nullable(dt)
                        | None -> System.Nullable()),
                    (fun nullable -> if nullable.HasValue then Some(nullable.Value) else None)
                )

            entity.Property(fun u -> u.Id).HasConversion(userIdConverter) |> ignore

            entity.Property(fun u -> u.ProfilePicUrl).HasConversion(optionStringConverter)
            |> ignore

            entity.Property(fun u -> u.InvitedAt).HasConversion(optionDateTimeConverter)
            |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun u -> not u.IsDeleted) |> ignore)
        |> ignore

        // Configure FolderData
        modelBuilder.Entity<FolderData>(fun entity ->
            let folderIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.FolderId, System.Guid>(
                    (fun folderId -> folderId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.FolderId(guid))
                )

            let folderNameConverter =
                ValueConverter<Freetool.Domain.ValueObjects.FolderName, string>(
                    (fun folderName -> folderName.Value),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.FolderName.Create(Some str) with
                        | Ok validName -> validName
                        | Error _ -> failwith $"Invalid FolderName in database: {str}")
                )

            let spaceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.SpaceId, System.Guid>(
                    (fun spaceId -> spaceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.SpaceId(guid))
                )

            let optionFolderIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.FolderId option, System.Nullable<System.Guid>>(
                    (fun opt ->
                        match opt with
                        | Some folderId -> System.Nullable(folderId.Value)
                        | None -> System.Nullable()),
                    (fun nullable ->
                        if nullable.HasValue then
                            Some(Freetool.Domain.ValueObjects.FolderId(nullable.Value))
                        else
                            None)
                )

            // Explicit property configuration to help with constructor binding
            entity.Property(fun f -> f.Id).HasColumnName("Id").HasConversion(folderIdConverter)
            |> ignore

            entity.Property(fun f -> f.Name).HasColumnName("Name").HasConversion(folderNameConverter)
            |> ignore

            entity.Property(fun f -> f.SpaceId).HasColumnName("SpaceId").HasConversion(spaceIdConverter)
            |> ignore

            entity.Property(fun f -> f.CreatedAt).HasColumnName("CreatedAt") |> ignore
            entity.Property(fun f -> f.UpdatedAt).HasColumnName("UpdatedAt") |> ignore
            entity.Property(fun f -> f.IsDeleted).HasColumnName("IsDeleted") |> ignore

            // Ignore the Children navigation property explicitly
            entity.Ignore("Children") |> ignore

            entity.Property(fun f -> f.ParentId).HasConversion(optionFolderIdConverter)
            |> ignore

            // Configure foreign key relationship to space
            entity.HasOne<SpaceData>().WithMany().HasForeignKey(fun f -> f.SpaceId :> obj)
            |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun f -> not f.IsDeleted) |> ignore)
        |> ignore

        // Configure RunData
        modelBuilder.Entity<RunData>(fun entity ->
            let runIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.RunId, System.Guid>(
                    (fun runId -> runId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.RunId(guid))
                )

            let appIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.AppId, System.Guid>(
                    (fun appId -> appId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.AppId(guid))
                )

            let runStatusConverter =
                ValueConverter<Freetool.Domain.ValueObjects.RunStatus, string>(
                    (fun runStatus -> runStatus.ToString()),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.RunStatus.Create(str) with
                        | Ok validStatus -> validStatus
                        | Error _ -> failwith $"Invalid RunStatus in database: {str}")
                )

            let optionDateTimeConverter =
                ValueConverter<DateTime option, System.Nullable<DateTime>>(
                    (fun opt ->
                        match opt with
                        | Some dt -> System.Nullable(dt)
                        | None -> System.Nullable()),
                    (fun nullable -> if nullable.HasValue then Some(nullable.Value) else None)
                )

            entity.Property(fun r -> r.Id).HasConversion(runIdConverter) |> ignore
            entity.Property(fun r -> r.AppId).HasConversion(appIdConverter) |> ignore
            entity.Property(fun r -> r.Status).HasConversion(runStatusConverter) |> ignore

            // JSON converter for InputValues list
            let runInputValueListConverter =
                ValueConverter<Freetool.Domain.RunInputValue list, string>(
                    (fun inputValues ->
                        let serializable =
                            inputValues |> List.map (fun iv -> {| Title = iv.Title; Value = iv.Value |})

                        System.Text.Json.JsonSerializer.Serialize(serializable)),
                    (fun json ->
                        if System.String.IsNullOrEmpty(json) then
                            []
                        else
                            let deserialized =
                                System.Text.Json.JsonSerializer.Deserialize<{| Title: string; Value: string |} list>(
                                    json
                                )

                            deserialized
                            |> List.map (fun item -> {
                                Freetool.Domain.RunInputValue.Title = item.Title
                                Freetool.Domain.RunInputValue.Value = item.Value
                            }))
                )

            entity.Property(fun r -> r.InputValues).HasConversion(runInputValueListConverter)
            |> ignore

            // JSON converter for ExecutableHttpRequest option
            let executableHttpRequestConverter =
                ValueConverter<Freetool.Domain.ExecutableHttpRequest option, string>(
                    ExecutableHttpRequestSerializer.serialize,
                    ExecutableHttpRequestSerializer.deserialize
                )

            entity.Property(fun r -> r.ExecutableRequest).HasConversion(executableHttpRequestConverter)
            |> ignore

            entity.Property(fun r -> r.ExecutedSql).HasConversion(optionStringConverter)
            |> ignore

            entity.Property(fun r -> r.StartedAt).HasConversion<System.Nullable<DateTime>>(optionDateTimeConverter)
            |> ignore

            entity.Property(fun r -> r.CompletedAt).HasConversion<System.Nullable<DateTime>>(optionDateTimeConverter)
            |> ignore

            entity.HasOne<AppData>().WithMany().HasForeignKey(fun r -> r.AppId :> obj)
            |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun r -> not r.IsDeleted) |> ignore)
        |> ignore

        // Configure ResourceData
        modelBuilder.Entity<ResourceData>(fun entity ->

            let resourceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.ResourceId, System.Guid>(
                    (fun resourceId -> resourceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.ResourceId(guid))
                )

            let resourceNameConverter =
                ValueConverter<Freetool.Domain.ValueObjects.ResourceName, string>(
                    (fun resourceName -> resourceName.Value),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.ResourceName.Create(Some str) with
                        | Ok validName -> validName
                        | Error _ -> failwith $"Invalid ResourceName in database: {str}")
                )

            let resourceDescriptionConverter =
                ValueConverter<Freetool.Domain.ValueObjects.ResourceDescription, string>(
                    (fun resourceDescription -> resourceDescription.Value),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.ResourceDescription.Create(Some str) with
                        | Ok validDescription -> validDescription
                        | Error _ -> failwith $"Invalid ResourceDescription in database: {str}")
                )

            let spaceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.SpaceId, System.Guid>(
                    (fun spaceId -> spaceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.SpaceId(guid))
                )

            let keyValuePairListConverter =
                ValueConverter<Freetool.Domain.ValueObjects.KeyValuePair list, string>(
                    (fun kvps ->
                        let serializable =
                            kvps |> List.map (fun kvp -> {| Key = kvp.Key; Value = kvp.Value |})

                        System.Text.Json.JsonSerializer.Serialize(serializable)),
                    (fun json ->
                        let deserialized =
                            System.Text.Json.JsonSerializer.Deserialize<{| Key: string; Value: string |} list>(json)

                        deserialized
                        |> List.map (fun item ->
                            match Freetool.Domain.ValueObjects.KeyValuePair.Create(item.Key, item.Value) with
                            | Ok kvp -> kvp
                            | Error _ -> failwith $"Invalid KeyValuePair in database: {item.Key}={item.Value}"))
                )

            let baseUrlConverter =
                ValueConverter<Freetool.Domain.ValueObjects.BaseUrl option, string>(
                    (fun opt ->
                        match opt with
                        | Some baseUrl -> baseUrl.Value
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.BaseUrl.Create(Some str) with
                            | Ok validBaseUrl -> Some validBaseUrl
                            | Error _ -> failwith $"Invalid BaseUrl in database: {str}")
                )

            let resourceKindConverter =
                ValueConverter<Freetool.Domain.ValueObjects.ResourceKind, string>(
                    (fun resourceKind -> resourceKind.ToString()),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.ResourceKind.Create(str) with
                        | Ok validKind -> validKind
                        | Error _ -> failwith $"Invalid ResourceKind in database: {str}")
                )

            let databaseNameConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabaseName option, string>(
                    (fun opt ->
                        match opt with
                        | Some name -> name.Value
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabaseName.Create(Some str) with
                            | Ok validName -> Some validName
                            | Error _ -> failwith $"Invalid DatabaseName in database: {str}")
                )

            let databaseHostConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabaseHost option, string>(
                    (fun opt ->
                        match opt with
                        | Some host -> host.Value
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabaseHost.Create(Some str) with
                            | Ok validHost -> Some validHost
                            | Error _ -> failwith $"Invalid DatabaseHost in database: {str}")
                )

            let databasePortConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabasePort option, System.Nullable<int>>(
                    (fun opt ->
                        match opt with
                        | Some port -> System.Nullable(port.Value)
                        | None -> System.Nullable()),
                    (fun nullable ->
                        if nullable.HasValue then
                            match Freetool.Domain.ValueObjects.DatabasePort.Create(Some nullable.Value) with
                            | Ok validPort -> Some validPort
                            | Error _ -> failwith $"Invalid DatabasePort in database: {nullable.Value}"
                        else
                            None)
                )

            let databaseEngineConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabaseEngine option, string>(
                    (fun opt ->
                        match opt with
                        | Some engine -> engine.ToString()
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabaseEngine.Create(str) with
                            | Ok validEngine -> Some validEngine
                            | Error _ -> failwith $"Invalid DatabaseEngine in database: {str}")
                )

            let databaseAuthSchemeConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabaseAuthScheme option, string>(
                    (fun opt ->
                        match opt with
                        | Some scheme -> scheme.ToString()
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabaseAuthScheme.Create(str) with
                            | Ok validScheme -> Some validScheme
                            | Error _ -> failwith $"Invalid DatabaseAuthScheme in database: {str}")
                )

            let databaseUsernameConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabaseUsername option, string>(
                    (fun opt ->
                        match opt with
                        | Some username -> username.Value
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabaseUsername.Create(Some str) with
                            | Ok validUsername -> Some validUsername
                            | Error _ -> failwith $"Invalid DatabaseUsername in database: {str}")
                )

            let databasePasswordConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DatabasePassword option, string>(
                    (fun opt ->
                        match opt with
                        | Some password -> password.Value
                        | None -> null),
                    (fun str ->
                        if isNull str then
                            None
                        else
                            match Freetool.Domain.ValueObjects.DatabasePassword.Create(Some str) with
                            | Ok validPassword -> Some validPassword
                            | Error _ -> failwith $"Invalid DatabasePassword in database")
                )

            entity.Property(fun r -> r.Id).HasConversion(resourceIdConverter) |> ignore
            entity.Property(fun r -> r.Name).HasConversion(resourceNameConverter) |> ignore

            entity.Property(fun r -> r.Description).HasConversion(resourceDescriptionConverter)
            |> ignore

            entity.Property(fun r -> r.SpaceId).HasConversion(spaceIdConverter) |> ignore

            entity.Property(fun r -> r.ResourceKind).HasConversion(resourceKindConverter)
            |> ignore

            entity.Property(fun r -> r.BaseUrl).HasConversion(baseUrlConverter) |> ignore

            entity.Property(fun r -> r.UrlParameters).HasConversion(keyValuePairListConverter)
            |> ignore

            entity.Property(fun r -> r.Headers).HasConversion(keyValuePairListConverter)
            |> ignore

            entity.Property(fun r -> r.Body).HasConversion(keyValuePairListConverter)
            |> ignore

            entity.Property(fun r -> r.DatabaseName).HasConversion(databaseNameConverter)
            |> ignore

            entity.Property(fun r -> r.DatabaseHost).HasConversion(databaseHostConverter)
            |> ignore

            entity.Property(fun r -> r.DatabasePort).HasConversion(databasePortConverter)
            |> ignore

            entity.Property(fun r -> r.DatabaseEngine).HasConversion(databaseEngineConverter)
            |> ignore

            entity.Property(fun r -> r.DatabaseAuthScheme).HasConversion(databaseAuthSchemeConverter)
            |> ignore

            entity.Property(fun r -> r.DatabaseUsername).HasConversion(databaseUsernameConverter)
            |> ignore

            entity.Property(fun r -> r.DatabasePassword).HasConversion(databasePasswordConverter)
            |> ignore

            entity.Property(fun r -> r.ConnectionOptions).HasConversion(keyValuePairListConverter)
            |> ignore

            // Configure foreign key relationship to space
            entity.HasOne<SpaceData>().WithMany().HasForeignKey(fun r -> r.SpaceId :> obj)
            |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun r -> not r.IsDeleted) |> ignore)
        |> ignore

        // Configure AppData
        modelBuilder.Entity<AppData>(fun entity ->
            let appIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.AppId, System.Guid>(
                    (fun appId -> appId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.AppId(guid))
                )

            let folderIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.FolderId, System.Guid>(
                    (fun folderId -> folderId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.FolderId(guid))
                )

            let resourceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.ResourceId, System.Guid>(
                    (fun resourceId -> resourceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.ResourceId(guid))
                )

            // Explicit property configuration to help with constructor binding
            entity.Property(fun a -> a.Id).HasColumnName("Id").HasConversion(appIdConverter)
            |> ignore

            entity.Property(fun a -> a.FolderId).HasConversion(folderIdConverter) |> ignore

            entity.Property(fun a -> a.ResourceId).HasColumnName("ResourceId").HasConversion(resourceIdConverter)
            |> ignore

            let httpMethodConverter =
                ValueConverter<Freetool.Domain.ValueObjects.HttpMethod, string>(
                    (fun httpMethod -> httpMethod.ToString()),
                    (fun str ->
                        match Freetool.Domain.ValueObjects.HttpMethod.Create(str) with
                        | Ok validMethod -> validMethod
                        | Error _ -> failwith $"Invalid HttpMethod in database: {str}")
                )

            entity.Property(fun a -> a.HttpMethod).HasConversion(httpMethodConverter)
            |> ignore

            entity.Property(fun a -> a.CreatedAt).HasColumnName("CreatedAt") |> ignore
            entity.Property(fun a -> a.UpdatedAt).HasColumnName("UpdatedAt") |> ignore
            entity.Property(fun a -> a.IsDeleted).HasColumnName("IsDeleted") |> ignore

            // JSON converters for complex list types
            let serializeInputs (inputs: Freetool.Domain.Events.Input list) =
                let serializable =
                    inputs
                    |> List.map (fun input -> {|
                        Title = input.Title
                        Description = input.Description |> Option.toObj
                        Type = input.Type.ToString()
                        Required = input.Required
                        DefaultValue = input.DefaultValue |> Option.map (fun dv -> dv.ToRawString()) |> Option.toObj
                    |})

                System.Text.Json.JsonSerializer.Serialize(serializable)

            let deserializeInputs (json: string) =
                let deserialized =
                    System.Text.Json.JsonSerializer.Deserialize<
                        {|
                            Title: string
                            Description: string
                            Type: string
                            Required: bool
                            DefaultValue: string
                        |} list
                     >(
                        json
                    )

                deserialized
                |> List.map (fun item ->
                    let inputType =
                        match item.Type with
                        | "Email" -> Ok(InputType.Email())
                        | "Date" -> Ok(InputType.Date())
                        | "Integer" -> Ok(InputType.Integer())
                        | "Boolean" -> Ok(InputType.Boolean())
                        | typeStr when typeStr.StartsWith("Currency(") && typeStr.EndsWith(")") ->
                            let currencyStr = typeStr.Substring(9, typeStr.Length - 10)

                            match currencyStr with
                            | "USD" -> Ok(InputType.Currency(SupportedCurrency.USD))
                            | _ ->
                                Error(
                                    Freetool.Domain.ValidationError
                                        $"Unknown Currency type format in database: {typeStr}"
                                )
                        | typeStr when typeStr.StartsWith("Text(") && typeStr.EndsWith(")") ->
                            let lengthStr = typeStr.Substring(5, typeStr.Length - 6)

                            match System.Int32.TryParse(lengthStr) with
                            | (true, maxLength) -> InputType.Text(maxLength)
                            | _ ->
                                Error(
                                    Freetool.Domain.ValidationError $"Invalid Text type format in database: {typeStr}"
                                )
                        | typeStr when typeStr.StartsWith("Radio([") && typeStr.EndsWith("])") ->
                            let optionsStr = typeStr.Substring(7, typeStr.Length - 9)

                            if System.String.IsNullOrWhiteSpace(optionsStr) then
                                Error(Freetool.Domain.ValidationError "Radio must have options")
                            else
                                let options =
                                    optionsStr.Split([| "\", \"" |], System.StringSplitOptions.None)
                                    |> Array.map (fun part ->
                                        let trimmed = part.Trim('"')

                                        if trimmed.Contains("\":\"") then
                                            let colonIdx = trimmed.IndexOf("\":\"")
                                            let value = trimmed.Substring(0, colonIdx)
                                            let label = trimmed.Substring(colonIdx + 3)

                                            {
                                                Freetool.Domain.ValueObjects.RadioOption.Value = value
                                                Label = Some label
                                            }
                                        else
                                            {
                                                Freetool.Domain.ValueObjects.RadioOption.Value = trimmed
                                                Label = None
                                            })
                                    |> Array.toList

                                InputType.Radio(options)
                        | _ ->
                            Error(
                                Freetool.Domain.ValidationError $"Unknown InputType format in database: {item.Type}"
                            )

                    match inputType with
                    | Ok validInputType ->
                        // Parse default value if present
                        let defaultValue =
                            if System.String.IsNullOrEmpty(item.DefaultValue) then
                                None
                            else
                                match DefaultValue.Create(validInputType, item.DefaultValue) with
                                | Ok dv -> Some dv
                                | Error _ -> None // Ignore invalid defaults in database

                        let description =
                            if System.String.IsNullOrWhiteSpace(item.Description) then
                                None
                            else
                                Some item.Description

                        {
                            Freetool.Domain.Events.Input.Title = item.Title
                            Freetool.Domain.Events.Input.Description = description
                            Freetool.Domain.Events.Input.Type = validInputType
                            Freetool.Domain.Events.Input.Required = item.Required
                            Freetool.Domain.Events.Input.DefaultValue = defaultValue
                        }
                    | Error _ -> failwith $"Invalid InputType in database: {item.Type}")

            let inputListConverter =
                ValueConverter<Freetool.Domain.Events.Input list, string>(serializeInputs, deserializeInputs)

            // InputType converter for complex discriminated union stored as JSON
            let serializeInputType (inputType: InputType) = inputType.ToString()

            let deserializeInputType (str: string) =
                match str with
                | "Email" -> InputType.Email()
                | "Date" -> InputType.Date()
                | "Integer" -> InputType.Integer()
                | "Boolean" -> InputType.Boolean()
                | typeStr when typeStr.StartsWith("Currency(") && typeStr.EndsWith(")") ->
                    let currencyStr = typeStr.Substring(9, typeStr.Length - 10)

                    match currencyStr with
                    | "USD" -> InputType.Currency(SupportedCurrency.USD)
                    | _ -> failwith $"Unknown Currency type format in database: {typeStr}"
                | typeStr when typeStr.StartsWith("Text(") && typeStr.EndsWith(")") ->
                    let lengthStr = typeStr.Substring(5, typeStr.Length - 6)

                    match System.Int32.TryParse(lengthStr) with
                    | (true, maxLength) ->
                        match InputType.Text(maxLength) with
                        | Ok validInputType -> validInputType
                        | Error _ -> failwith $"Invalid Text type format in database: {typeStr}"
                    | _ -> failwith $"Invalid Text type format in database: {typeStr}"
                | typeStr when typeStr.StartsWith("Radio([") && typeStr.EndsWith("])") ->
                    let optionsStr = typeStr.Substring(7, typeStr.Length - 9)

                    if System.String.IsNullOrWhiteSpace(optionsStr) then
                        failwith "Radio must have options"
                    else
                        let options =
                            optionsStr.Split([| "\", \"" |], System.StringSplitOptions.None)
                            |> Array.map (fun part ->
                                let trimmed = part.Trim('"')

                                if trimmed.Contains("\":\"") then
                                    let colonIdx = trimmed.IndexOf("\":\"")
                                    let value = trimmed.Substring(0, colonIdx)
                                    let label = trimmed.Substring(colonIdx + 3)

                                    {
                                        Freetool.Domain.ValueObjects.RadioOption.Value = value
                                        Label = Some label
                                    }
                                else
                                    {
                                        Freetool.Domain.ValueObjects.RadioOption.Value = trimmed
                                        Label = None
                                    })
                            |> Array.toList

                        match InputType.Radio(options) with
                        | Ok validInputType -> validInputType
                        | Error _ -> failwith $"Invalid Radio type format in database: {typeStr}"
                | _ -> failwith $"Unknown InputType format in database: {str}"

            let inputTypeConverter =
                ValueConverter<InputType, string>(serializeInputType, deserializeInputType)

            let keyValuePairListConverter =
                ValueConverter<Freetool.Domain.ValueObjects.KeyValuePair list, string>(
                    (fun kvps ->
                        let serializable =
                            kvps |> List.map (fun kvp -> {| Key = kvp.Key; Value = kvp.Value |})

                        System.Text.Json.JsonSerializer.Serialize(serializable)),
                    (fun json ->
                        let deserialized =
                            System.Text.Json.JsonSerializer.Deserialize<{| Key: string; Value: string |} list>(json)

                        deserialized
                        |> List.map (fun item ->
                            match Freetool.Domain.ValueObjects.KeyValuePair.Create(item.Key, item.Value) with
                            | Ok kvp -> kvp
                            | Error _ -> failwith $"Invalid KeyValuePair in database: {item.Key}={item.Value}"))
                )

            entity.Property(fun a -> a.Inputs).HasConversion<string>(inputListConverter)
            |> ignore

            entity.Property(fun a -> a.UrlPath).HasConversion(optionStringConverter)
            |> ignore

            entity.Property(fun a -> a.UrlParameters).HasConversion(keyValuePairListConverter)
            |> ignore

            entity.Property(fun a -> a.Headers).HasConversion(keyValuePairListConverter)
            |> ignore

            entity.Property(fun a -> a.Body).HasConversion(keyValuePairListConverter)
            |> ignore

            let sqlModeToString (mode: Freetool.Domain.Entities.SqlQueryMode) =
                match mode with
                | Freetool.Domain.Entities.SqlQueryMode.Gui -> "gui"
                | Freetool.Domain.Entities.SqlQueryMode.Raw -> "raw"

            let sqlModeFromString (value: string) =
                match value.Trim().ToLowerInvariant() with
                | "gui" -> Freetool.Domain.Entities.SqlQueryMode.Gui
                | "raw" -> Freetool.Domain.Entities.SqlQueryMode.Raw
                | _ -> failwith $"Invalid SqlQueryMode in database: {value}"

            let sqlOperatorToString (op: Freetool.Domain.Entities.SqlFilterOperator) =
                match op with
                | Freetool.Domain.Entities.SqlFilterOperator.Equals -> "="
                | Freetool.Domain.Entities.SqlFilterOperator.NotEquals -> "!="
                | Freetool.Domain.Entities.SqlFilterOperator.GreaterThan -> ">"
                | Freetool.Domain.Entities.SqlFilterOperator.GreaterThanOrEqual -> ">="
                | Freetool.Domain.Entities.SqlFilterOperator.LessThan -> "<"
                | Freetool.Domain.Entities.SqlFilterOperator.LessThanOrEqual -> "<="
                | Freetool.Domain.Entities.SqlFilterOperator.Like -> "LIKE"
                | Freetool.Domain.Entities.SqlFilterOperator.ILike -> "ILIKE"
                | Freetool.Domain.Entities.SqlFilterOperator.In -> "IN"
                | Freetool.Domain.Entities.SqlFilterOperator.NotIn -> "NOT IN"
                | Freetool.Domain.Entities.SqlFilterOperator.IsNull -> "IS NULL"
                | Freetool.Domain.Entities.SqlFilterOperator.IsNotNull -> "IS NOT NULL"

            let sqlOperatorFromString (value: string) =
                match value.Trim().ToUpperInvariant() with
                | "=" -> Freetool.Domain.Entities.SqlFilterOperator.Equals
                | "!="
                | "<>" -> Freetool.Domain.Entities.SqlFilterOperator.NotEquals
                | ">" -> Freetool.Domain.Entities.SqlFilterOperator.GreaterThan
                | ">=" -> Freetool.Domain.Entities.SqlFilterOperator.GreaterThanOrEqual
                | "<" -> Freetool.Domain.Entities.SqlFilterOperator.LessThan
                | "<=" -> Freetool.Domain.Entities.SqlFilterOperator.LessThanOrEqual
                | "LIKE" -> Freetool.Domain.Entities.SqlFilterOperator.Like
                | "ILIKE" -> Freetool.Domain.Entities.SqlFilterOperator.ILike
                | "IN" -> Freetool.Domain.Entities.SqlFilterOperator.In
                | "NOT IN" -> Freetool.Domain.Entities.SqlFilterOperator.NotIn
                | "IS NULL" -> Freetool.Domain.Entities.SqlFilterOperator.IsNull
                | "IS NOT NULL" -> Freetool.Domain.Entities.SqlFilterOperator.IsNotNull
                | _ -> failwith $"Invalid SqlFilterOperator in database: {value}"

            let sqlDirectionToString (direction: Freetool.Domain.Entities.SqlSortDirection) =
                match direction with
                | Freetool.Domain.Entities.SqlSortDirection.Asc -> "ASC"
                | Freetool.Domain.Entities.SqlSortDirection.Desc -> "DESC"

            let sqlDirectionFromString (value: string) =
                match value.Trim().ToUpperInvariant() with
                | "ASC" -> Freetool.Domain.Entities.SqlSortDirection.Asc
                | "DESC" -> Freetool.Domain.Entities.SqlSortDirection.Desc
                | _ -> failwith $"Invalid SqlSortDirection in database: {value}"

            let sqlConfigConverter =
                ValueConverter<Freetool.Domain.Entities.SqlQueryConfig option, string>(
                    (fun configOpt ->
                        match configOpt with
                        | None -> null
                        | Some config ->
                            let serialized = {|
                                Mode = sqlModeToString config.Mode
                                Table = config.Table
                                Columns = config.Columns
                                Filters =
                                    config.Filters
                                    |> List.map (fun filter -> {|
                                        Column = filter.Column
                                        Operator = sqlOperatorToString filter.Operator
                                        Value = filter.Value
                                    |})
                                Limit = config.Limit
                                OrderBy =
                                    config.OrderBy
                                    |> List.map (fun orderBy -> {|
                                        Column = orderBy.Column
                                        Direction = sqlDirectionToString orderBy.Direction
                                    |})
                                RawSql = config.RawSql
                                RawSqlParams =
                                    config.RawSqlParams
                                    |> List.map (fun kvp -> {| Key = kvp.Key; Value = kvp.Value |})
                            |}

                            System.Text.Json.JsonSerializer.Serialize(serialized)),
                    (fun json ->
                        if System.String.IsNullOrWhiteSpace(json) then
                            None
                        else
                            let deserialized =
                                System.Text.Json.JsonSerializer.Deserialize<
                                    {|
                                        Mode: string
                                        Table: string option
                                        Columns: string list
                                        Filters:
                                            {|
                                                Column: string
                                                Operator: string
                                                Value: string option
                                            |} list
                                        Limit: int option
                                        OrderBy: {| Column: string; Direction: string |} list
                                        RawSql: string option
                                        RawSqlParams: {| Key: string; Value: string |} list
                                    |}
                                 >(
                                    json
                                )

                            let filters =
                                deserialized.Filters
                                |> List.map (fun filter -> {
                                    Freetool.Domain.Entities.SqlFilter.Column = filter.Column
                                    Operator = sqlOperatorFromString filter.Operator
                                    Value = filter.Value
                                })

                            let orderBy =
                                deserialized.OrderBy
                                |> List.map (fun order -> {
                                    Freetool.Domain.Entities.SqlOrderBy.Column = order.Column
                                    Direction = sqlDirectionFromString order.Direction
                                })

                            let rawParams =
                                deserialized.RawSqlParams
                                |> List.map (fun kvp ->
                                    match Freetool.Domain.ValueObjects.KeyValuePair.Create(kvp.Key, kvp.Value) with
                                    | Ok valid -> valid
                                    | Error _ -> failwith $"Invalid KeyValuePair in database: {kvp.Key}={kvp.Value}")

                            Some {
                                Freetool.Domain.Entities.SqlQueryConfig.Mode = sqlModeFromString deserialized.Mode
                                Table = deserialized.Table
                                Columns = deserialized.Columns
                                Filters = filters
                                Limit = deserialized.Limit
                                OrderBy = orderBy
                                RawSql = deserialized.RawSql
                                RawSqlParams = rawParams
                            })
                )

            entity.Property(fun a -> a.SqlConfig).HasConversion(sqlConfigConverter)
            |> ignore

            entity.Property(fun a -> a.Description).HasConversion(optionStringConverter)
            |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun a -> not a.IsDeleted) |> ignore)
        |> ignore

        // Configure DashboardData
        modelBuilder.Entity<DashboardData>(fun entity ->
            let dashboardIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DashboardId, System.Guid>(
                    (fun dashboardId -> dashboardId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.DashboardId(guid))
                )

            let dashboardNameConverter =
                ValueConverter<Freetool.Domain.ValueObjects.DashboardName, string>(
                    (fun dashboardName -> dashboardName.Value),
                    (fun str ->
                        match DashboardName.Create(Some str) with
                        | Ok validName -> validName
                        | Error _ -> failwith $"Invalid DashboardName in database: {str}")
                )

            let folderIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.FolderId, System.Guid>(
                    (fun folderId -> folderId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.FolderId(guid))
                )

            let optionAppIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.AppId option, System.Nullable<System.Guid>>(
                    (fun opt ->
                        match opt with
                        | Some appId -> System.Nullable(appId.Value)
                        | None -> System.Nullable()),
                    (fun nullable ->
                        if nullable.HasValue then
                            Some(Freetool.Domain.ValueObjects.AppId(nullable.Value))
                        else
                            None)
                )

            entity.Property(fun d -> d.Id).HasConversion(dashboardIdConverter) |> ignore

            entity.Property(fun d -> d.Name).HasConversion(dashboardNameConverter) |> ignore

            entity.Property(fun d -> d.FolderId).HasConversion(folderIdConverter) |> ignore

            entity.Property(fun d -> d.PrepareAppId).HasConversion(optionAppIdConverter)
            |> ignore

            entity.Property(fun d -> d.CreatedAt).HasColumnName("CreatedAt") |> ignore
            entity.Property(fun d -> d.UpdatedAt).HasColumnName("UpdatedAt") |> ignore
            entity.Property(fun d -> d.IsDeleted).HasColumnName("IsDeleted") |> ignore

            entity.HasOne<FolderData>().WithMany().HasForeignKey(fun d -> d.FolderId :> obj)
            |> ignore

            entity.HasQueryFilter(fun d -> not d.IsDeleted) |> ignore)
        |> ignore

        // Configure EventData
        modelBuilder.Entity<EventData>(fun entity ->
            let eventTypeConverter =
                ValueConverter<EventType, string>(
                    (fun eventType -> EventTypeConverter.toString eventType),
                    (fun str ->
                        match EventTypeConverter.fromString str with
                        | Some eventType -> eventType
                        | None -> failwith $"Invalid EventType in database: {str}")
                )

            let entityTypeConverter =
                ValueConverter<EntityType, string>(
                    (fun entityType -> EntityTypeConverter.toString entityType),
                    (fun str ->
                        match EntityTypeConverter.fromString str with
                        | Some entityType -> entityType
                        | None -> failwith $"Invalid EntityType in database: {str}")
                )

            let userIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.UserId, System.Guid>(
                    (fun userId -> userId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.UserId(guid))
                )

            // Explicit property configuration to help with constructor binding
            entity.Property(fun e -> e.Id).HasColumnName("Id") |> ignore
            entity.Property(fun e -> e.EventId).HasColumnName("EventId") |> ignore

            entity.Property(fun e -> e.EventType).HasColumnName("EventType").HasConversion(eventTypeConverter)
            |> ignore

            entity.Property(fun e -> e.EntityType).HasColumnName("EntityType").HasConversion(entityTypeConverter)
            |> ignore

            entity.Property(fun e -> e.EntityId).HasColumnName("EntityId") |> ignore
            entity.Property(fun e -> e.EventData).HasColumnName("EventData") |> ignore
            entity.Property(fun e -> e.OccurredAt).HasColumnName("OccurredAt") |> ignore
            entity.Property(fun e -> e.CreatedAt).HasColumnName("CreatedAt") |> ignore
            entity.Property(fun e -> e.UserId).HasConversion(userIdConverter) |> ignore)
        |> ignore

        // Configure SpaceData
        modelBuilder.Entity<SpaceData>(fun entity ->
            let spaceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.SpaceId, System.Guid>(
                    (fun spaceId -> spaceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.SpaceId(guid))
                )

            let userIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.UserId, System.Guid>(
                    (fun userId -> userId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.UserId(guid))
                )

            // Explicit property configuration to help with constructor binding
            entity.Property(fun s -> s.Id).HasColumnName("Id").HasConversion(spaceIdConverter)
            |> ignore

            entity.Property(fun s -> s.Name).HasColumnName("Name").HasMaxLength(100).IsRequired()
            |> ignore

            entity
                .Property(fun s -> s.ModeratorUserId)
                .HasColumnName("ModeratorUserId")
                .HasConversion(userIdConverter)
                .IsRequired()
            |> ignore

            entity.Property(fun s -> s.CreatedAt).HasColumnName("CreatedAt") |> ignore
            entity.Property(fun s -> s.UpdatedAt).HasColumnName("UpdatedAt") |> ignore
            entity.Property(fun s -> s.IsDeleted).HasColumnName("IsDeleted") |> ignore

            // Ignore the MemberIds navigation properties explicitly (populated via SpaceMembers junction table)
            entity.Ignore("MemberIds") |> ignore
            entity.Ignore("_memberIds") |> ignore

            // Global query filter for soft delete
            entity.HasQueryFilter(fun s -> not s.IsDeleted) |> ignore)
        |> ignore

        // Configure SpaceMemberData
        modelBuilder.Entity<SpaceMemberData>(fun entity ->
            let userIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.UserId, System.Guid>(
                    (fun userId -> userId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.UserId(guid))
                )

            let spaceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.SpaceId, System.Guid>(
                    (fun spaceId -> spaceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.SpaceId(guid))
                )

            entity.Property(fun sm -> sm.UserId).HasConversion(userIdConverter) |> ignore
            entity.Property(fun sm -> sm.SpaceId).HasConversion(spaceIdConverter) |> ignore

            // Configure foreign key relationships
            entity.HasOne<UserData>().WithMany().HasForeignKey(fun sm -> sm.UserId :> obj)
            |> ignore

            entity.HasOne<SpaceData>().WithMany().HasForeignKey(fun sm -> sm.SpaceId :> obj)
            |> ignore)
        |> ignore

        // Configure IdentityGroupSpaceMappingData
        modelBuilder.Entity<IdentityGroupSpaceMappingData>(fun entity ->
            let userIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.UserId, System.Guid>(
                    (fun userId -> userId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.UserId(guid))
                )

            let spaceIdConverter =
                ValueConverter<Freetool.Domain.ValueObjects.SpaceId, System.Guid>(
                    (fun spaceId -> spaceId.Value),
                    (fun guid -> Freetool.Domain.ValueObjects.SpaceId(guid))
                )

            entity.ToTable("IdentityGroupSpaceMappings") |> ignore
            entity.HasKey("Id") |> ignore
            entity.HasIndex("GroupKey", "SpaceId").IsUnique() |> ignore
            entity.Property(fun m -> m.GroupKey).HasMaxLength(200).IsRequired() |> ignore

            entity.Property(fun m -> m.SpaceId).HasConversion(spaceIdConverter).IsRequired()
            |> ignore

            entity.Property(fun m -> m.IsActive).IsRequired() |> ignore

            entity.Property(fun m -> m.CreatedByUserId).HasConversion(userIdConverter).IsRequired()
            |> ignore

            entity.Property(fun m -> m.UpdatedByUserId).HasConversion(userIdConverter).IsRequired()
            |> ignore

            entity.Property(fun m -> m.CreatedAt).IsRequired() |> ignore
            entity.Property(fun m -> m.UpdatedAt).IsRequired() |> ignore)
        |> ignore