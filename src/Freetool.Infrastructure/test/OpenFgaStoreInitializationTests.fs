module Freetool.Infrastructure.Tests.OpenFgaStoreInitializationTests

open System
open Xunit
open Microsoft.Extensions.Logging
open Freetool.Api.Services

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