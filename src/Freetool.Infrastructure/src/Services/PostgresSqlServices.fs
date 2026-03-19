namespace Freetool.Infrastructure.Services

open System
open System.Threading.Tasks
open System.Text.Json
open Npgsql
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Interfaces

module private PostgresSqlHelpers =
    let private requireValue name valueOption =
        match valueOption with
        | Some value -> Ok value
        | None -> Error(InvalidOperation $"SQL resource is missing {name}")

    let buildConnectionString (resource: ResourceData) : Result<string, DomainError> =
        match resource.ResourceKind, resource.DatabaseEngine with
        | ResourceKind.Sql, Some DatabaseEngine.Postgres ->
            match
                requireValue "database name" resource.DatabaseName,
                requireValue "database host" resource.DatabaseHost,
                requireValue "database port" resource.DatabasePort,
                requireValue "database username" resource.DatabaseUsername
            with
            | Error err, _, _, _
            | _, Error err, _, _
            | _, _, Error err, _
            | _, _, _, Error err -> Error err
            | Ok databaseName, Ok host, Ok port, Ok username ->
                let builder = NpgsqlConnectionStringBuilder()
                builder.Host <- host.Value
                builder.Port <- port.Value
                builder.Database <- databaseName.Value
                builder.Username <- username.Value

                match resource.DatabasePassword with
                | Some password -> builder.Password <- password.Value
                | None -> ()

                if resource.UseSsl then
                    builder.SslMode <- SslMode.Require
                    builder.TrustServerCertificate <- true
                else
                    builder.SslMode <- SslMode.Disable

                resource.ConnectionOptions
                |> List.iter (fun kvp ->
                    try
                        builder.[kvp.Key] <- kvp.Value
                    with _ ->
                        ())

                Ok builder.ConnectionString
        | ResourceKind.Sql, _ -> Error(ValidationError "Unsupported database engine for SQL resource")
        | _ -> Error(InvalidOperation "Resource is not a SQL resource")

type PostgresSqlExecutionService() =
    interface ISqlExecutionService with
        member _.ExecuteQueryAsync (resource: ResourceData) (query: SqlQuery) : Task<Result<string, DomainError>> = task {
            let trimmed = query.Sql.TrimStart()

            if
                not (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                && not (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            then
                return Error(InvalidOperation "Only SELECT queries are allowed for SQL resources")
            else
                match PostgresSqlHelpers.buildConnectionString resource with
                | Error err -> return Error err
                | Ok connectionString ->
                    try
                        use conn = new NpgsqlConnection(connectionString)
                        do! conn.OpenAsync()

                        use cmd = new NpgsqlCommand(query.Sql, conn)

                        query.Parameters
                        |> List.iter (fun (name, value) ->
                            let paramName = if name.StartsWith("@") then name else $"@{name}"
                            cmd.Parameters.AddWithValue(paramName, value) |> ignore)

                        use! reader = cmd.ExecuteReaderAsync()

                        let rows = ResizeArray<obj>()
                        let fieldCount = reader.FieldCount

                        while reader.Read() do
                            let row = System.Collections.Generic.Dictionary<string, obj>()

                            for i in 0 .. (fieldCount - 1) do
                                let name = reader.GetName(i)
                                let value = if reader.IsDBNull(i) then null else reader.GetValue(i)
                                row.[name] <- value

                            rows.Add(row)

                        let json = JsonSerializer.Serialize(rows)
                        return Ok json
                    with ex ->
                        return Error(InvalidOperation $"SQL query failed: {ex.Message}")
        }

type PostgresSqlMetadataService() =
    interface ISqlMetadataService with
        member _.GetTablesAsync(resource: ResourceData) : Task<Result<SqlTableInfoDto list, DomainError>> = task {
            match PostgresSqlHelpers.buildConnectionString resource with
            | Error err -> return Error err
            | Ok connectionString ->
                try
                    use conn = new NpgsqlConnection(connectionString)
                    do! conn.OpenAsync()

                    use cmd =
                        new NpgsqlCommand(
                            "select table_schema, table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema not in ('pg_catalog', 'information_schema') order by table_schema, table_name",
                            conn
                        )

                    use! reader = cmd.ExecuteReaderAsync()
                    let tables = ResizeArray<SqlTableInfoDto>()

                    while reader.Read() do
                        tables.Add(
                            {
                                Schema = reader.GetString(0)
                                Name = reader.GetString(1)
                            }
                        )

                    return Ok(tables |> Seq.toList)
                with ex ->
                    return Error(InvalidOperation $"Failed to fetch SQL tables: {ex.Message}")
        }

        member _.GetColumnsAsync
            (resource: ResourceData)
            (tableName: string)
            : Task<Result<SqlColumnInfoDto list, DomainError>> =
            task {
                match PostgresSqlHelpers.buildConnectionString resource with
                | Error err -> return Error err
                | Ok connectionString ->
                    try
                        let schema, table =
                            if tableName.Contains(".") then
                                let parts = tableName.Split([| '.' |], 2)
                                parts.[0], parts.[1]
                            else
                                "public", tableName

                        use conn = new NpgsqlConnection(connectionString)
                        do! conn.OpenAsync()

                        use cmd =
                            new NpgsqlCommand(
                                "select column_name, data_type, is_nullable from information_schema.columns where table_schema = @schema and table_name = @table order by ordinal_position",
                                conn
                            )

                        cmd.Parameters.AddWithValue("@schema", schema) |> ignore
                        cmd.Parameters.AddWithValue("@table", table) |> ignore

                        use! reader = cmd.ExecuteReaderAsync()
                        let columns = ResizeArray<SqlColumnInfoDto>()

                        while reader.Read() do
                            let isNullable =
                                match reader.GetString(2).ToUpperInvariant() with
                                | "YES" -> true
                                | _ -> false

                            columns.Add(
                                {
                                    Name = reader.GetString(0)
                                    DataType = reader.GetString(1)
                                    IsNullable = isNullable
                                }
                            )

                        return Ok(columns |> Seq.toList)
                    with ex ->
                        return Error(InvalidOperation $"Failed to fetch SQL columns: {ex.Message}")
            }