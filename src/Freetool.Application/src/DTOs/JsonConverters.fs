namespace Freetool.Application.DTOs

open System
open System.Text.Json
open System.Text.Json.Serialization
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type StringOptionConverter() =
    inherit JsonConverter<string option>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        if reader.TokenType = JsonTokenType.Null then
            None
        else
            let str = reader.GetString()

            match str with
            | "" -> None
            | nonEmptyString -> Some nonEmptyString

    override _.Write(writer: Utf8JsonWriter, value: string option, _options: JsonSerializerOptions) =
        match value with
        | None -> writer.WriteNullValue()
        | Some str ->
            if String.IsNullOrEmpty(str) then
                writer.WriteNullValue()
            else
                writer.WriteStringValue(str)

type UserIdConverter() =
    inherit JsonConverter<UserId>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let guidStr = reader.GetString()
        let guid = Guid.Parse(guidStr)
        UserId.FromGuid(guid)

    override _.Write(writer: Utf8JsonWriter, value: UserId, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.Value.ToString())

type HttpMethodConverter() =
    inherit JsonConverter<HttpMethod>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let methodStr = reader.GetString()

        match HttpMethod.Create(methodStr) with
        | Ok httpMethod -> httpMethod
        | Error _ -> raise (JsonException($"Invalid HTTP method: {methodStr}"))

    override _.Write(writer: Utf8JsonWriter, value: HttpMethod, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.ToString())

type ResourceKindConverter() =
    inherit JsonConverter<ResourceKind>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let kindStr = reader.GetString()

        match ResourceKind.Create(kindStr) with
        | Ok resourceKind -> resourceKind
        | Error _ -> raise (JsonException($"Invalid resource kind: {kindStr}"))

    override _.Write(writer: Utf8JsonWriter, value: ResourceKind, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.ToString())

type DatabaseAuthSchemeConverter() =
    inherit JsonConverter<DatabaseAuthScheme>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let schemeStr = reader.GetString()

        match DatabaseAuthScheme.Create(schemeStr) with
        | Ok scheme -> scheme
        | Error _ -> raise (JsonException($"Invalid database auth scheme: {schemeStr}"))

    override _.Write(writer: Utf8JsonWriter, value: DatabaseAuthScheme, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.ToString())

type DatabaseEngineConverter() =
    inherit JsonConverter<DatabaseEngine>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let engineStr = reader.GetString()

        match DatabaseEngine.Create(engineStr) with
        | Ok engine -> engine
        | Error _ -> raise (JsonException($"Invalid database engine: {engineStr}"))

    override _.Write(writer: Utf8JsonWriter, value: DatabaseEngine, _options: JsonSerializerOptions) =
        writer.WriteStringValue(value.ToString())

type SqlQueryModeConverter() =
    inherit JsonConverter<SqlQueryMode>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let modeStr = reader.GetString()

        match modeStr.Trim().ToLowerInvariant() with
        | "gui" -> SqlQueryMode.Gui
        | "raw" -> SqlQueryMode.Raw
        | _ -> raise (JsonException($"Invalid SQL query mode: {modeStr}"))

    override _.Write(writer: Utf8JsonWriter, value: SqlQueryMode, _options: JsonSerializerOptions) =
        let stringValue =
            match value with
            | SqlQueryMode.Gui -> "gui"
            | SqlQueryMode.Raw -> "raw"

        writer.WriteStringValue(stringValue)

type SqlFilterOperatorConverter() =
    inherit JsonConverter<SqlFilterOperator>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let opStr = reader.GetString()

        match opStr.Trim().ToUpperInvariant() with
        | "=" -> SqlFilterOperator.Equals
        | "!="
        | "<>" -> SqlFilterOperator.NotEquals
        | ">" -> SqlFilterOperator.GreaterThan
        | ">=" -> SqlFilterOperator.GreaterThanOrEqual
        | "<" -> SqlFilterOperator.LessThan
        | "<=" -> SqlFilterOperator.LessThanOrEqual
        | "LIKE" -> SqlFilterOperator.Like
        | "ILIKE" -> SqlFilterOperator.ILike
        | "IN" -> SqlFilterOperator.In
        | "NOT IN" -> SqlFilterOperator.NotIn
        | "IS NULL" -> SqlFilterOperator.IsNull
        | "IS NOT NULL" -> SqlFilterOperator.IsNotNull
        | _ -> raise (JsonException($"Invalid SQL filter operator: {opStr}"))

    override _.Write(writer: Utf8JsonWriter, value: SqlFilterOperator, _options: JsonSerializerOptions) =
        let stringValue =
            match value with
            | SqlFilterOperator.Equals -> "="
            | SqlFilterOperator.NotEquals -> "!="
            | SqlFilterOperator.GreaterThan -> ">"
            | SqlFilterOperator.GreaterThanOrEqual -> ">="
            | SqlFilterOperator.LessThan -> "<"
            | SqlFilterOperator.LessThanOrEqual -> "<="
            | SqlFilterOperator.Like -> "LIKE"
            | SqlFilterOperator.ILike -> "ILIKE"
            | SqlFilterOperator.In -> "IN"
            | SqlFilterOperator.NotIn -> "NOT IN"
            | SqlFilterOperator.IsNull -> "IS NULL"
            | SqlFilterOperator.IsNotNull -> "IS NOT NULL"

        writer.WriteStringValue(stringValue)

type SqlSortDirectionConverter() =
    inherit JsonConverter<SqlSortDirection>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let directionStr = reader.GetString()

        match directionStr.Trim().ToUpperInvariant() with
        | "ASC" -> SqlSortDirection.Asc
        | "DESC" -> SqlSortDirection.Desc
        | _ -> raise (JsonException($"Invalid SQL sort direction: {directionStr}"))

    override _.Write(writer: Utf8JsonWriter, value: SqlSortDirection, _options: JsonSerializerOptions) =
        let stringValue =
            match value with
            | SqlSortDirection.Asc -> "ASC"
            | SqlSortDirection.Desc -> "DESC"

        writer.WriteStringValue(stringValue)

type EventTypeConverter() =
    inherit JsonConverter<EventType>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let eventTypeStr = reader.GetString()

        match Freetool.Domain.Entities.EventTypeConverter.fromString (eventTypeStr) with
        | Some eventType -> eventType
        | None -> raise (JsonException($"Invalid event type: {eventTypeStr}"))

    override _.Write(writer: Utf8JsonWriter, value: EventType, _options: JsonSerializerOptions) =
        writer.WriteStringValue(Freetool.Domain.Entities.EventTypeConverter.toString (value))

type EntityTypeConverter() =
    inherit JsonConverter<EntityType>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        let entityTypeStr = reader.GetString()

        match Freetool.Domain.Entities.EntityTypeConverter.fromString (entityTypeStr) with
        | Some entityType -> entityType
        | None -> raise (JsonException($"Invalid entity type: {entityTypeStr}"))

    override _.Write(writer: Utf8JsonWriter, value: EntityType, _options: JsonSerializerOptions) =
        writer.WriteStringValue(Freetool.Domain.Entities.EntityTypeConverter.toString (value))

type KeyValuePairConverter() =
    inherit JsonConverter<KeyValuePair>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        if reader.TokenType <> JsonTokenType.StartObject then
            raise (JsonException("Expected start of object for KeyValuePair"))

        let mutable key = ""
        let mutable value = ""

        while reader.Read() do
            match reader.TokenType with
            | JsonTokenType.PropertyName ->
                let propertyName = reader.GetString()
                reader.Read() |> ignore

                match propertyName with
                | "key" -> key <- reader.GetString()
                | "value" -> value <- reader.GetString()
                | _ -> reader.Skip()
            | JsonTokenType.EndObject -> ()
            | _ -> reader.Skip()

        match KeyValuePair.Create(key, value) with
        | Ok kvp -> kvp
        | Error err -> raise (JsonException($"Failed to create KeyValuePair: {err}"))

    override _.Write(writer: Utf8JsonWriter, value: KeyValuePair, _options: JsonSerializerOptions) =
        writer.WriteStartObject()
        writer.WriteString("key", value.Key)
        writer.WriteString("value", value.Value)
        writer.WriteEndObject()

type FolderLocationConverter() =
    inherit JsonConverter<FolderLocation>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) =
        if reader.TokenType = JsonTokenType.Null then
            RootFolder
        else
            let str = reader.GetString()

            if String.IsNullOrWhiteSpace(str) then
                RootFolder
            else
                ChildFolder str

    override _.Write(writer: Utf8JsonWriter, value: FolderLocation, _options: JsonSerializerOptions) =
        match value with
        | RootFolder -> writer.WriteNullValue()
        | ChildFolder parentId -> writer.WriteStringValue(parentId)