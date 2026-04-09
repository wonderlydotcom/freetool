namespace Freetool.Api.Services

open System
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Freetool.Api
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Database
open Freetool.Infrastructure.Services

type OpenFgaStoreInitializationDependencies = {
    GetSavedStoreId: unit -> string option
    SaveStoreId: string -> unit
    StoreExists: string -> bool
    CreateStore: unit -> string
    Sleep: TimeSpan -> unit
}

exception OpenFgaStoreMissingException of string

module OpenFgaStoreInitialization =
    let createDependencies (connectionString: string) (apiUrl: string) : OpenFgaStoreInitializationDependencies =
        let createAuthorizationService () =
            let tempService = OpenFgaService(apiUrl, NullLogger<OpenFgaService>.Instance)
            tempService :> IAuthorizationService

        {
            GetSavedStoreId = fun () -> SettingsStore.get connectionString ConfigurationKeys.OpenFGA.StoreId
            SaveStoreId = fun storeId -> SettingsStore.set connectionString ConfigurationKeys.OpenFGA.StoreId storeId
            StoreExists =
                fun storeId ->
                    let existsTask = (createAuthorizationService ()).StoreExistsAsync(storeId)
                    existsTask.Wait()
                    existsTask.Result
            CreateStore =
                fun () ->
                    let storeTask =
                        (createAuthorizationService ()).CreateStoreAsync({ Name = "freetool-authorization" })

                    storeTask.Wait()
                    storeTask.Result.Id
            Sleep = fun delay -> System.Threading.Thread.Sleep(delay)
        }

    let private createAndSaveNewStore
        (logger: ILogger)
        (dependencies: OpenFgaStoreInitializationDependencies)
        : string =
        logger.LogInformation("Creating new OpenFGA store...")
        let newStoreId = dependencies.CreateStore()
        logger.LogInformation("Created new OpenFGA store with ID: {StoreId}", newStoreId)
        dependencies.SaveStoreId newStoreId
        logger.LogInformation("Saved OpenFGA store ID to database")
        newStoreId

    let private failForMissingStore (logger: ILogger) (storeSource: string) (storeId: string) : string =
        let message =
            $"OpenFGA {storeSource} store {storeId} does not exist. Refusing to create a replacement store automatically because that would orphan existing authorization data. Restore the original OpenFGA database or clear the saved store ID intentionally before retrying startup."

        logger.LogCritical("{Message}", message)
        raise (OpenFgaStoreMissingException message)

    let ensureOpenFgaStore
        (logger: ILogger)
        (dependencies: OpenFgaStoreInitializationDependencies)
        (configuredStoreId: string)
        (allowAutoCreateWhenPersistedStoreMissing: bool)
        : string =
        match dependencies.GetSavedStoreId() with
        | Some storeId when not (String.IsNullOrEmpty(storeId)) ->
            logger.LogInformation("Found OpenFGA store ID in database: {StoreId}", storeId)

            if dependencies.StoreExists storeId then
                logger.LogInformation("OpenFGA store {StoreId} exists, using it", storeId)
                storeId
            elif allowAutoCreateWhenPersistedStoreMissing then
                logger.LogWarning(
                    "OpenFGA store {StoreId} no longer exists. Creating a new store because automatic recovery is enabled.",
                    storeId
                )

                createAndSaveNewStore logger dependencies
            else
                failForMissingStore logger "database" storeId
        | _ ->
            if String.IsNullOrEmpty(configuredStoreId) then
                logger.LogInformation("No OpenFGA store ID configured. Creating new store...")
                createAndSaveNewStore logger dependencies
            else
                logger.LogInformation("Checking if configured OpenFGA store {StoreId} exists...", configuredStoreId)

                if dependencies.StoreExists configuredStoreId then
                    logger.LogInformation(
                        "OpenFGA store {StoreId} exists, using it and saving to database",
                        configuredStoreId
                    )

                    dependencies.SaveStoreId configuredStoreId
                    configuredStoreId
                elif allowAutoCreateWhenPersistedStoreMissing then
                    logger.LogWarning(
                        "OpenFGA store {StoreId} does not exist. Creating a new store because automatic recovery is enabled.",
                        configuredStoreId
                    )

                    createAndSaveNewStore logger dependencies
                else
                    failForMissingStore logger "configured" configuredStoreId

    let ensureOpenFgaStoreWithRetry
        (logger: ILogger)
        (dependencies: OpenFgaStoreInitializationDependencies)
        (configuredStoreId: string)
        (allowAutoCreateWhenPersistedStoreMissing: bool)
        : string =
        let maxAttempts = 20
        let retryDelay = TimeSpan.FromSeconds(2.0)

        let rec attempt currentAttempt =
            try
                ensureOpenFgaStore logger dependencies configuredStoreId allowAutoCreateWhenPersistedStoreMissing
            with ex ->
                if ex :? OpenFgaStoreMissingException then
                    raise ex
                elif currentAttempt >= maxAttempts then
                    raise ex
                else
                    logger.LogWarning(
                        ex,
                        "OpenFGA store initialization failed on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds} seconds...",
                        currentAttempt,
                        maxAttempts,
                        retryDelay.TotalSeconds
                    )

                    dependencies.Sleep retryDelay
                    attempt (currentAttempt + 1)

        attempt 1