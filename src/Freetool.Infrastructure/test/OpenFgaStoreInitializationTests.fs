module Freetool.Infrastructure.Tests.OpenFgaStoreInitializationTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Xunit
open Microsoft.Extensions.Logging
open Freetool.Api
open Freetool.Api.Services
open Freetool.Infrastructure.Database

let private openFgaApiUrl = "http://localhost:8090"

type CapturingLogger() =
    let mutable infos: string list = []
    let mutable warnings: string list = []
    let mutable criticals: string list = []

    member _.Infos = infos
    member _.Warnings = warnings
    member _.Criticals = criticals

    interface ILogger with
        member _.BeginScope<'State>(_state: 'State) = null
        member _.IsEnabled(_logLevel: LogLevel) = true

        member _.Log<'State>
            (logLevel: LogLevel, _eventId: EventId, state: 'State, exn: exn, formatter: Func<'State, exn, string>)
            =
            let message = formatter.Invoke(state, exn)

            match logLevel with
            | LogLevel.Critical -> criticals <- message :: criticals
            | LogLevel.Warning -> warnings <- message :: warnings
            | LogLevel.Information -> infos <- message :: infos
            | _ -> ()

[<Literal>]
let private settingsTableSql =
    """
    CREATE TABLE Settings (
        Key TEXT PRIMARY KEY,
        Value TEXT NULL,
        CreatedAt TEXT NOT NULL,
        UpdatedAt TEXT NOT NULL
    )
    """

let private createSettingsDatabase () =
    let path = Path.Combine(Path.GetTempPath(), $"openfga-init-{Guid.NewGuid()}.db")
    let connectionString = $"Data Source={path}"

    use connection = new SqliteConnection(connectionString)
    connection.Open()

    use command = connection.CreateCommand()
    command.CommandText <- settingsTableSql
    command.ExecuteNonQuery() |> ignore

    connectionString, path

[<Fact>]
let ``ensureOpenFgaStoreWithRetry retries transient store lookup failures without creating a new store`` () =
    let logger = CapturingLogger()
    let mutable storeExistsCalls = 0
    let mutable createStoreCalls = 0
    let mutable sleepCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> Some "store-123"
        SaveStoreId = fun _ -> ()
        StoreExists =
            fun _ ->
                storeExistsCalls <- storeExistsCalls + 1

                if storeExistsCalls < 3 then
                    raise (InvalidOperationException "connection refused")

                true
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = fun _ -> sleepCalls <- sleepCalls + 1
    }

    let actualStoreId =
        OpenFgaStoreInitialization.ensureOpenFgaStoreWithRetry (logger :> ILogger) dependencies "" false

    Assert.Equal("store-123", actualStoreId)
    Assert.Equal(3, storeExistsCalls)
    Assert.Equal(0, createStoreCalls)
    Assert.Equal(2, sleepCalls)
    Assert.Contains(logger.Warnings, fun message -> message.Contains("Retrying"))

[<Fact>]
let ``ensureOpenFgaStore uses existing persisted store when it still exists`` () =
    let logger = CapturingLogger()
    let mutable saveStoreCalls = 0
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> Some "store-123"
        SaveStoreId = fun _ -> saveStoreCalls <- saveStoreCalls + 1
        StoreExists =
            fun storeId ->
                Assert.Equal("store-123", storeId)
                true
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = ignore
    }

    let actualStoreId =
        OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "" false

    Assert.Equal("store-123", actualStoreId)
    Assert.Equal(0, saveStoreCalls)
    Assert.Equal(0, createStoreCalls)
    Assert.Contains(logger.Infos, fun message -> message.Contains("exists, using it"))

[<Fact>]
let ``ensureOpenFgaStore fails loudly when persisted store is missing and automatic recovery is disabled`` () =
    let logger = CapturingLogger()
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> Some "store-123"
        SaveStoreId = fun _ -> ()
        StoreExists = fun _ -> false
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = ignore
    }

    let ex =
        Assert.Throws<OpenFgaStoreMissingException>(fun () ->
            OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "" false
            |> ignore)

    Assert.Contains("Refusing to create a replacement store automatically", ex.Message)
    Assert.Equal(0, createStoreCalls)
    Assert.Contains(logger.Criticals, fun message -> message.Contains("store-123"))

[<Fact>]
let ``ensureOpenFgaStore uses existing configured store and persists it`` () =
    let logger = CapturingLogger()
    let mutable savedStoreId = None
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> None
        SaveStoreId = fun storeId -> savedStoreId <- Some storeId
        StoreExists =
            fun storeId ->
                Assert.Equal("configured-store", storeId)
                true
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = ignore
    }

    let actualStoreId =
        OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "configured-store" false

    Assert.Equal("configured-store", actualStoreId)
    Assert.Equal(Some "configured-store", savedStoreId)
    Assert.Equal(0, createStoreCalls)

[<Fact>]
let ``ensureOpenFgaStore fails loudly when configured store is missing and automatic recovery is disabled`` () =
    let logger = CapturingLogger()
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> None
        SaveStoreId = fun _ -> ()
        StoreExists = fun _ -> false
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = ignore
    }

    let ex =
        Assert.Throws<OpenFgaStoreMissingException>(fun () ->
            OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "configured-store" false
            |> ignore)

    Assert.Contains("configured store configured-store does not exist", ex.Message)
    Assert.Equal(0, createStoreCalls)

[<Fact>]
let ``ensureOpenFgaStore creates and saves a new store when no store is configured`` () =
    let logger = CapturingLogger()
    let mutable savedStoreId = None
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> None
        SaveStoreId = fun storeId -> savedStoreId <- Some storeId
        StoreExists = fun _ -> failwith "StoreExists should not be called when no store is configured"
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "new-store"
        Sleep = ignore
    }

    let actualStoreId =
        OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "" false

    Assert.Equal("new-store", actualStoreId)
    Assert.Equal(Some "new-store", savedStoreId)
    Assert.Equal(1, createStoreCalls)
    Assert.Contains(logger.Infos, fun message -> message.Contains("Created new OpenFGA store with ID"))

[<Fact>]
let ``ensureOpenFgaStore can recreate a missing configured store when automatic recovery is enabled`` () =
    let logger = CapturingLogger()
    let mutable savedStoreId = None
    let mutable createStoreCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> None
        SaveStoreId = fun storeId -> savedStoreId <- Some storeId
        StoreExists =
            fun storeId ->
                Assert.Equal("configured-store", storeId)
                false
        CreateStore =
            fun () ->
                createStoreCalls <- createStoreCalls + 1
                "replacement-store"
        Sleep = ignore
    }

    let actualStoreId =
        OpenFgaStoreInitialization.ensureOpenFgaStore (logger :> ILogger) dependencies "configured-store" true

    Assert.Equal("replacement-store", actualStoreId)
    Assert.Equal(Some "replacement-store", savedStoreId)
    Assert.Equal(1, createStoreCalls)
    Assert.Contains(logger.Warnings, fun message -> message.Contains("automatic recovery is enabled"))

[<Fact>]
let ``resolveActualStoreId falls back to configured store in development when initialization fails`` () =
    let logger = CapturingLogger()
    let mutable sleepCalls = 0

    let dependencies = {
        GetSavedStoreId = fun () -> raise (InvalidOperationException "boom")
        SaveStoreId = fun _ -> ()
        StoreExists = fun _ -> true
        CreateStore = fun () -> "new-store"
        Sleep = fun _ -> sleepCalls <- sleepCalls + 1
    }

    let actualStoreId =
        OpenFgaStoreInitialization.resolveActualStoreId (logger :> ILogger) dependencies "configured-store" false true

    Assert.Equal("configured-store", actualStoreId)
    Assert.Equal(19, sleepCalls)
    Assert.Contains(logger.Warnings, fun message -> message.Contains("Could not ensure OpenFGA store exists"))

[<Fact>]
let ``resolveActualStoreId rethrows in non-development when initialization fails`` () =
    let logger = CapturingLogger()

    let dependencies = {
        GetSavedStoreId = fun () -> raise (InvalidOperationException "boom")
        SaveStoreId = fun _ -> ()
        StoreExists = fun _ -> true
        CreateStore = fun () -> "new-store"
        Sleep = ignore
    }

    let ex =
        Assert.Throws<InvalidOperationException>(fun () ->
            OpenFgaStoreInitialization.resolveActualStoreId
                (logger :> ILogger)
                dependencies
                "configured-store"
                false
                false
            |> ignore)

    Assert.Equal("boom", ex.Message)
    Assert.Contains(logger.Criticals, fun message -> message.Contains("Refusing to start"))

[<Fact>]
let ``createDependencies can create persist and detect a store using local OpenFGA`` () : Task = task {
    let connectionString, path = createSettingsDatabase ()

    try
        let dependencies =
            OpenFgaStoreInitialization.createDependencies connectionString openFgaApiUrl

        let createdStoreId = dependencies.CreateStore()

        let mutable exists = false

        for _ in 1..5 do
            exists <- dependencies.StoreExists createdStoreId

            if not exists then
                do! Task.Delay(100)

        Assert.True(exists)

        dependencies.SaveStoreId createdStoreId
        Assert.Equal(Some createdStoreId, dependencies.GetSavedStoreId())
    finally
        if File.Exists(path) then
            File.Delete(path)
}

[<Fact>]
let ``resolveActualStoreIdFromRuntime creates and persists a store on first boot`` () : Task = task {
    let logger = CapturingLogger()
    let connectionString, path = createSettingsDatabase ()

    try
        let actualStoreId =
            OpenFgaStoreInitialization.resolveActualStoreIdFromRuntime
                (logger :> ILogger)
                connectionString
                openFgaApiUrl
                ""
                true

        Assert.False(String.IsNullOrWhiteSpace(actualStoreId))

        let savedStoreId =
            SettingsStore.get connectionString ConfigurationKeys.OpenFGA.StoreId

        Assert.Equal(Some actualStoreId, savedStoreId)
    finally
        if File.Exists(path) then
            File.Delete(path)
}