namespace Freetool.Infrastructure.Database

open System
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore.Storage

type SqliteExecutionStrategy(dependencies: ExecutionStrategyDependencies) =
    inherit ExecutionStrategy(dependencies, 3, TimeSpan.FromSeconds 2.0)

    override _.ShouldRetryOn(ex: Exception) =
        match ex with
        | :? SqliteException as sqliteException ->
            let message = sqliteException.Message

            sqliteException.SqliteErrorCode = 5
            || message.IndexOf("database is locked", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf(
                "unable to delete/modify user-function due to active statements",
                StringComparison.OrdinalIgnoreCase
               )
               >= 0
            || message.IndexOf("not an error", StringComparison.OrdinalIgnoreCase) >= 0
        | _ -> false

type SqliteExecutionStrategyFactory(dependencies: ExecutionStrategyDependencies) =
    interface IExecutionStrategyFactory with
        member _.Create() =
            SqliteExecutionStrategy(dependencies) :> IExecutionStrategy