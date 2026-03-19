namespace Freetool.Infrastructure.Database

open System.Data.Common
open System.Reflection
open DbUp
open Microsoft.Data.Sqlite

module Persistence =

    type IDBIOAsync<'a> = (DbConnection -> Async<'a>) -> Async<'a>

    let getDatabaseConnectionString (dataSource: string) =
        let builder = SqliteConnectionStringBuilder()
        builder.DataSource <- dataSource
        builder.ToString()

    let getDbConnectionAsync connectionString : Async<DbConnection> = async {
        let dbConnection = new SqliteConnection(connectionString)
        return dbConnection
    }

    let getDatabaseConnection connectionString : DbConnection = new SqliteConnection(connectionString)

    let withDbIoAsync connectionString (callbackAsync: DbConnection -> Async<'a>) = async {
        use dbConnection = new SqliteConnection(connectionString)
        do! dbConnection.OpenAsync() |> Async.AwaitTask
        return! callbackAsync dbConnection
    }

    let configureSqliteWal (connectionString: string) =
        use connection = new SqliteConnection(connectionString)
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA journal_mode=WAL;"
        command.ExecuteNonQuery() |> ignore
        command.CommandText <- "PRAGMA busy_timeout=5000;"
        command.ExecuteNonQuery() |> ignore

    let upgradeDatabase (connectionString: string) =
        // SQLite creates the database file automatically if it doesn't exist
        let upgrader =
            DeployChanges.To
                .SQLiteDatabase(connectionString: string)
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    (fun (scriptName: string) ->
                        printfn "DBUp found script: %s" scriptName
                        scriptName.Contains("DatabaseUpgradeScripts.DBUP"))
                )
                .WithTransactionPerScript()
                .LogToConsole()
                .Build()

        let result = upgrader.PerformUpgrade()

        match result.Successful with
        | false ->
            printfn "Database upgrade failed!"

            match result.Error with
            | null -> ()
            | ex -> printfn "Error: %s" ex.Message

            failwith "Failed to upgrade database!"
        | true ->
            printfn "Database upgrade successful!"
            configureSqliteWal connectionString