namespace Freetool.Infrastructure.Database

open System
open Microsoft.Data.Sqlite

/// Simple key-value settings store using raw SQLite access
/// Used for early startup configuration (before DI is available)
module SettingsStore =

    /// Gets a setting value by key
    let get (connectionString: string) (key: string) : string option =
        use connection = new SqliteConnection(connectionString)
        connection.Open()

        use command = connection.CreateCommand()
        command.CommandText <- "SELECT Value FROM Settings WHERE Key = @Key"
        command.Parameters.AddWithValue("@Key", key) |> ignore

        let result = command.ExecuteScalar()

        if isNull result || result = box DBNull.Value then
            None
        else
            Some(result :?> string)

    /// Sets a setting value (insert or update)
    let set (connectionString: string) (key: string) (value: string) : unit =
        use connection = new SqliteConnection(connectionString)
        connection.Open()

        let now = DateTime.UtcNow.ToString("o")

        use command = connection.CreateCommand()

        command.CommandText <-
            """
            INSERT INTO Settings (Key, Value, CreatedAt, UpdatedAt)
            VALUES (@Key, @Value, @Now, @Now)
            ON CONFLICT(Key) DO UPDATE SET Value = @Value, UpdatedAt = @Now
            """

        command.Parameters.AddWithValue("@Key", key) |> ignore
        command.Parameters.AddWithValue("@Value", value) |> ignore
        command.Parameters.AddWithValue("@Now", now) |> ignore

        command.ExecuteNonQuery() |> ignore