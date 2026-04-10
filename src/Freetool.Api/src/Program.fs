open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.EntityFrameworkCore
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open System.Text.Json.Serialization
open System.Diagnostics
open System
open OpenTelemetry.Resources
open OpenTelemetry.Trace
open OpenTelemetry.Exporter
open Freetool.Infrastructure.Database
open Freetool.Infrastructure.Database.Repositories
open Freetool.Infrastructure.Services
open Freetool.Application.Interfaces
open Freetool.Application.Handlers
open Freetool.Application.Commands
open Freetool.Application.DTOs
open Freetool.Application.Services
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Api
open Freetool.Api.Auth
open Freetool.Api.Tracing
open Freetool.Api.Middleware
open Freetool.Api.OpenApi
open Freetool.Api.Services

/// Validates that all EventType cases can be serialized and deserialized consistently.
/// This catches bugs where a new event type is added to the DU but not to fromString.
let private validateEventTypeRegistry (logger: ILogger) =
    // List all known event types that should be registered
    let allEventTypes = [ // User events
        EventType.UserEvents UserCreatedEvent
        EventType.UserEvents UserUpdatedEvent
        EventType.UserEvents UserDeletedEvent
        EventType.UserEvents UserInvitedEvent
        EventType.UserEvents UserActivatedEvent
        // App events
        EventType.AppEvents AppCreatedEvent
        EventType.AppEvents AppUpdatedEvent
        EventType.AppEvents AppDeletedEvent
        EventType.AppEvents AppRestoredEvent
        // Dashboard events
        EventType.DashboardEvents DashboardCreatedEvent
        EventType.DashboardEvents DashboardUpdatedEvent
        EventType.DashboardEvents DashboardDeletedEvent
        EventType.DashboardEvents DashboardPreparedEvent
        EventType.DashboardEvents DashboardPrepareFailedEvent
        EventType.DashboardEvents DashboardActionExecutedEvent
        EventType.DashboardEvents DashboardActionFailedEvent
        // Resource events
        EventType.ResourceEvents ResourceCreatedEvent
        EventType.ResourceEvents ResourceUpdatedEvent
        EventType.ResourceEvents ResourceDeletedEvent
        EventType.ResourceEvents ResourceRestoredEvent
        // Folder events
        EventType.FolderEvents FolderCreatedEvent
        EventType.FolderEvents FolderUpdatedEvent
        EventType.FolderEvents FolderDeletedEvent
        EventType.FolderEvents FolderRestoredEvent
        // Run events
        EventType.RunEvents RunCreatedEvent
        EventType.RunEvents RunStatusChangedEvent
        // Space events
        EventType.SpaceEvents SpaceCreatedEvent
        EventType.SpaceEvents SpaceUpdatedEvent
        EventType.SpaceEvents SpaceDeletedEvent
        EventType.SpaceEvents SpacePermissionsChangedEvent
        EventType.SpaceEvents SpaceDefaultMemberPermissionsChangedEvent
    ]

    let mutable hasErrors = false

    for eventType in allEventTypes do
        let serialized = EventTypeConverter.toString eventType
        let deserialized = EventTypeConverter.fromString serialized

        match deserialized with
        | None ->
            logger.LogError(
                "EventTypeConverter.fromString cannot parse '{Serialized}' (from {EventType}). Add it to EventTypeConverter.fromString.",
                serialized,
                eventType
            )

            hasErrors <- true
        | Some parsed when parsed <> eventType ->
            logger.LogError(
                "EventTypeConverter round-trip failed for {EventType}: serialized='{Serialized}', deserialized={Deserialized}",
                eventType,
                serialized,
                parsed
            )

            hasErrors <- true
        | Some _ -> ()

    if hasErrors then
        failwith "EventType registry validation failed. See errors above."
    else
        logger.LogInformation("EventType registry validation passed ({Count} event types)", allEventTypes.Length)

/// Creates a new OpenFGA store and saves the ID to the database
let private createAndSaveNewStore (logger: ILogger) (connectionString: string) (apiUrl: string) : string =
    let tempService = OpenFgaService(apiUrl, NullLogger<OpenFgaService>.Instance)
    let authService = tempService :> IAuthorizationService

    logger.LogInformation("Creating new OpenFGA store...")
    let storeTask = authService.CreateStoreAsync({ Name = "freetool-authorization" })
    storeTask.Wait()
    let newStoreId = storeTask.Result.Id
    logger.LogInformation("Created new OpenFGA store with ID: {StoreId}", newStoreId)

    // Save to database for future restarts
    SettingsStore.set connectionString ConfigurationKeys.OpenFGA.StoreId newStoreId
    logger.LogInformation("Saved OpenFGA store ID to database")

    newStoreId

/// Checks if a store exists in OpenFGA
let private storeExists (apiUrl: string) (storeId: string) : bool =
    let tempService = OpenFgaService(apiUrl, NullLogger<OpenFgaService>.Instance)
    let authService = tempService :> IAuthorizationService
    let existsTask = authService.StoreExistsAsync(storeId)
    existsTask.Wait()
    existsTask.Result

/// Ensures an OpenFGA store exists, creating one if necessary
/// Persists the store ID to the database to survive restarts
/// Returns the store ID to use for the application
let ensureOpenFgaStore
    (logger: ILogger)
    (connectionString: string)
    (apiUrl: string)
    (configuredStoreId: string)
    : string =
    // First, check if we have a store ID saved in the database
    let dbStoreId = SettingsStore.get connectionString ConfigurationKeys.OpenFGA.StoreId

    match dbStoreId with
    | Some storeId when not (System.String.IsNullOrEmpty(storeId)) ->
        // We have a store ID in the database, verify it exists in OpenFGA
        logger.LogInformation("Found OpenFGA store ID in database: {StoreId}", storeId)

        if storeExists apiUrl storeId then
            logger.LogInformation("OpenFGA store {StoreId} exists, using it", storeId)
            storeId
        else
            // Store was deleted from OpenFGA, create a new one
            logger.LogInformation("OpenFGA store {StoreId} no longer exists. Creating new store...", storeId)
            createAndSaveNewStore logger connectionString apiUrl
    | _ ->
        // No store ID in database, check config
        if System.String.IsNullOrEmpty(configuredStoreId) then
            // No store configured anywhere, create a new one
            logger.LogInformation("No OpenFGA store ID configured. Creating new store...")
            createAndSaveNewStore logger connectionString apiUrl
        else
            // Check if configured store exists
            logger.LogInformation("Checking if configured OpenFGA store {StoreId} exists...", configuredStoreId)

            if storeExists apiUrl configuredStoreId then
                logger.LogInformation(
                    "OpenFGA store {StoreId} exists, using it and saving to database",
                    configuredStoreId
                )
                // Save the config store ID to database for future restarts
                SettingsStore.set connectionString ConfigurationKeys.OpenFGA.StoreId configuredStoreId
                configuredStoreId
            else
                // Configured store doesn't exist, create a new one
                logger.LogInformation(
                    "OpenFGA store {StoreId} does not exist. Creating new store...",
                    configuredStoreId
                )

                createAndSaveNewStore logger connectionString apiUrl

let private ensureOpenFgaStoreWithRetry
    (logger: ILogger)
    (connectionString: string)
    (apiUrl: string)
    (configuredStoreId: string)
    : string =
    let maxAttempts = 20
    let retryDelay = System.TimeSpan.FromSeconds(2.0)

    let rec attempt currentAttempt =
        try
            ensureOpenFgaStore logger connectionString apiUrl configuredStoreId
        with ex ->
            if currentAttempt >= maxAttempts then
                raise ex
            else
                logger.LogWarning(
                    ex,
                    "OpenFGA store initialization failed on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds} seconds...",
                    currentAttempt,
                    maxAttempts,
                    retryDelay.TotalSeconds
                )

                System.Threading.Thread.Sleep(retryDelay)
                attempt (currentAttempt + 1)

    attempt 1

[<System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage>]
[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let runtimeSecretsPath = "/var/run/secrets/app"

    builder.Configuration.AddKeyPerFile(runtimeSecretsPath, true) |> ignore

    if
        String.IsNullOrWhiteSpace(builder.Configuration[ConfigurationKeys.Auth.IAP.JwtAudience])
        && not (String.IsNullOrWhiteSpace(builder.Configuration[ConfigurationKeys.Auth.IAP.PlatformJwtAudience]))
    then
        builder.Configuration[ConfigurationKeys.Auth.IAP.JwtAudience] <-
            builder.Configuration[ConfigurationKeys.Auth.IAP.PlatformJwtAudience]

    // Create a startup logger for logging before app is built
    use startupLoggerFactory =
        LoggerFactory.Create(fun logging -> logging.AddConsole() |> ignore)

    let startupLogger = startupLoggerFactory.CreateLogger("Freetool.Startup")

    // Detect dev mode from environment variable
    let isDevMode =
        System.Environment.GetEnvironmentVariable(ConfigurationKeys.Environment.DevMode) = "true"

    if isDevMode then
        startupLogger.LogInformation("[DEV MODE] Running in development mode with user impersonation")

    // Add CORS for dev mode (allows frontend on different port)
    if isDevMode then
        builder.Services.AddCors(fun options ->
            options.AddPolicy(
                "DevCors",
                fun policy -> policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader() |> ignore
            ))
        |> ignore

    // Run database migrations early (before OpenFGA store check)
    // This ensures the Settings table exists for storing the store ID
    let connectionString =
        builder.Configuration.GetConnectionString(ConfigurationKeys.DefaultConnection)

    Persistence.upgradeDatabase connectionString

    // Validate that all EventType cases are properly registered
    validateEventTypeRegistry startupLogger

    // Add services to the container
    builder.Services
        .AddControllers(fun options -> options.SuppressAsyncSuffixInActionNames <- false)
        .ConfigureApiBehaviorOptions(fun options -> options.SuppressModelStateInvalidFilter <- false)
        .AddJsonOptions(fun options ->
            options.JsonSerializerOptions.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
            options.JsonSerializerOptions.PropertyNamingPolicy <- System.Text.Json.JsonNamingPolicy.CamelCase
            options.JsonSerializerOptions.Converters.Add(HttpMethodConverter())
            options.JsonSerializerOptions.Converters.Add(ResourceKindConverter())
            options.JsonSerializerOptions.Converters.Add(DatabaseAuthSchemeConverter())
            options.JsonSerializerOptions.Converters.Add(DatabaseEngineConverter())
            options.JsonSerializerOptions.Converters.Add(SqlQueryModeConverter())
            options.JsonSerializerOptions.Converters.Add(SqlFilterOperatorConverter())
            options.JsonSerializerOptions.Converters.Add(SqlSortDirectionConverter())
            options.JsonSerializerOptions.Converters.Add(EventTypeConverter())
            options.JsonSerializerOptions.Converters.Add(EntityTypeConverter())
            options.JsonSerializerOptions.Converters.Add(KeyValuePairConverter())
            options.JsonSerializerOptions.Converters.Add(FolderLocationConverter())

            // allowOverride = true lets type-level [<JsonFSharpConverter>] attributes take precedence
            options.JsonSerializerOptions.Converters.Add(JsonFSharpConverter(allowOverride = true)))
    |> ignore

    builder.Services.AddEndpointsApiExplorer() |> ignore

    builder.Services.AddHttpClient() |> ignore

    builder.Services.AddSwaggerGen(fun c ->
        c.SupportNonNullableReferenceTypes()
        c.UseAllOfToExtendReferenceSchemas()

        c.MapType<FolderLocation>(fun () -> Microsoft.OpenApi.Models.OpenApiSchema(Type = "string", Nullable = true))

        c.SchemaFilter<FSharpUnionSchemaFilter>()
        c.OperationFilter<FSharpQueryParameterOperationFilter>())
    |> ignore

    builder.Services.AddDbContext<FreetoolDbContext>(fun options ->
        options
            .UseSqlite(builder.Configuration.GetConnectionString ConfigurationKeys.DefaultConnection)
            .ReplaceService<
                Microsoft.EntityFrameworkCore.Storage.IExecutionStrategyFactory,
                SqliteExecutionStrategyFactory
              >()
        |> ignore)
    |> ignore

    builder.Services.AddScoped<IUserRepository>(fun serviceProvider ->
        let context = serviceProvider.GetRequiredService<FreetoolDbContext>()
        let eventRepository = serviceProvider.GetRequiredService<IEventRepository>()
        UserRepository(context, eventRepository))
    |> ignore

    builder.Services.AddScoped<ISpaceRepository>(fun serviceProvider ->
        let context = serviceProvider.GetRequiredService<FreetoolDbContext>()
        let eventRepository = serviceProvider.GetRequiredService<IEventRepository>()
        SpaceRepository(context, eventRepository))
    |> ignore

    builder.Services.AddScoped<IIdentityGroupSpaceMappingRepository>(fun serviceProvider ->
        let context = serviceProvider.GetRequiredService<FreetoolDbContext>()
        IdentityGroupSpaceMappingRepository(context))
    |> ignore

    builder.Services.AddScoped<IResourceRepository, ResourceRepository>() |> ignore
    builder.Services.AddScoped<IFolderRepository, FolderRepository>() |> ignore
    builder.Services.AddScoped<IAppRepository, AppRepository>() |> ignore

    builder.Services.AddScoped<IDashboardRepository, DashboardRepository>()
    |> ignore

    builder.Services.AddScoped<IRunRepository, RunRepository>() |> ignore
    builder.Services.AddScoped<IEventRepository, EventRepository>() |> ignore

    builder.Services.AddScoped<ISqlExecutionService, PostgresSqlExecutionService>()
    |> ignore

    builder.Services.AddScoped<ISqlMetadataService, PostgresSqlMetadataService>()
    |> ignore
    // Ensure OpenFGA store exists before registering the service
    // The store ID is persisted to the database to survive restarts
    let openFgaApiUrl = builder.Configuration[ConfigurationKeys.OpenFGA.ApiUrl]
    let configuredStoreId = builder.Configuration[ConfigurationKeys.OpenFGA.StoreId]

    let resolveOpenFgaStoreId =
        OpenFgaStoreInitialization.resolveActualStoreIdFromRuntime startupLogger connectionString openFgaApiUrl

    let actualStoreId =
        resolveOpenFgaStoreId configuredStoreId (builder.Environment.IsDevelopment())

    let createOpenFgaService (serviceProvider: IServiceProvider) =
        let loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>()
        let logger = loggerFactory.CreateLogger<OpenFgaService>()

        if System.String.IsNullOrEmpty(actualStoreId) then
            OpenFgaService(openFgaApiUrl, logger)
        else
            OpenFgaService(openFgaApiUrl, logger, actualStoreId)

    builder.Services.AddScoped<IAuthorizationService>(fun serviceProvider ->
        // Always create with the actual store ID (which may have been created)
        createOpenFgaService serviceProvider :> IAuthorizationService)
    |> ignore

    builder.Services.AddScoped<IAuthorizationRelationshipReader>(fun serviceProvider ->
        createOpenFgaService serviceProvider :> IAuthorizationRelationshipReader)
    |> ignore

    builder.Services.AddScoped<IEventEnhancementService>(fun serviceProvider ->
        let userRepository = serviceProvider.GetRequiredService<IUserRepository>()
        let appRepository = serviceProvider.GetRequiredService<IAppRepository>()
        let dashboardRepository = serviceProvider.GetRequiredService<IDashboardRepository>()
        let folderRepository = serviceProvider.GetRequiredService<IFolderRepository>()
        let resourceRepository = serviceProvider.GetRequiredService<IResourceRepository>()
        let spaceRepository = serviceProvider.GetRequiredService<ISpaceRepository>()

        EventEnhancementService(
            userRepository,
            appRepository,
            dashboardRepository,
            folderRepository,
            resourceRepository,
            spaceRepository
        )
        :> IEventEnhancementService)
    |> ignore

    builder.Services.AddScoped<IGoogleDirectoryIdentityService, GoogleDirectoryIdentityService>()
    |> ignore

    builder.Services.AddScoped<IIdentityProvisioningService, IdentityProvisioningService>()
    |> ignore

    builder.Services.AddScoped<IOpenFgaSpaceAuthorizationRepairService, OpenFgaSpaceAuthorizationRepairService>()
    |> ignore

    builder.Services.AddScoped<UserHandler>() |> ignore

    builder.Services.AddScoped<SpaceHandler>(fun serviceProvider ->
        let spaceRepository = serviceProvider.GetRequiredService<ISpaceRepository>()
        let userRepository = serviceProvider.GetRequiredService<IUserRepository>()
        let authService = serviceProvider.GetRequiredService<IAuthorizationService>()
        let eventRepository = serviceProvider.GetRequiredService<IEventRepository>()
        SpaceHandler(spaceRepository, userRepository, authService, eventRepository))
    |> ignore

    builder.Services.AddScoped<ResourceHandler>(fun serviceProvider ->
        let resourceRepository = serviceProvider.GetRequiredService<IResourceRepository>()
        let appRepository = serviceProvider.GetRequiredService<IAppRepository>()
        ResourceHandler(resourceRepository, appRepository))
    |> ignore

    builder.Services.AddScoped<FolderHandler>() |> ignore
    builder.Services.AddScoped<AppHandler>() |> ignore
    builder.Services.AddScoped<DashboardHandler>() |> ignore

    builder.Services.AddScoped<ICommandHandler<UserCommand, UserCommandResult>>(fun serviceProvider ->
        let userHandler = serviceProvider.GetRequiredService<UserHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "user" userHandler activitySource)
    |> ignore

    builder.Services.AddScoped<ICommandHandler<ResourceCommand, ResourceCommandResult>>(fun serviceProvider ->
        let resourceHandler = serviceProvider.GetRequiredService<ResourceHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "resource" resourceHandler activitySource)
    |> ignore

    builder.Services.AddScoped<ICommandHandler<SpaceCommand, SpaceCommandResult>>(fun serviceProvider ->
        let spaceHandler = serviceProvider.GetRequiredService<SpaceHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "space" spaceHandler activitySource)
    |> ignore

    builder.Services.AddScoped<ICommandHandler<FolderCommand, FolderCommandResult>>(fun serviceProvider ->
        let folderHandler = serviceProvider.GetRequiredService<FolderHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "folder" folderHandler activitySource)
    |> ignore

    builder.Services.AddScoped<ICommandHandler<AppCommand, AppCommandResult>>(fun serviceProvider ->
        let appHandler = serviceProvider.GetRequiredService<AppHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "app" appHandler activitySource)
    |> ignore

    builder.Services.AddScoped<ICommandHandler<DashboardCommand, DashboardCommandResult>>(fun serviceProvider ->
        let dashboardHandler = serviceProvider.GetRequiredService<DashboardHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "dashboard" dashboardHandler activitySource)
    |> ignore

    builder.Services.AddScoped<TrashHandler>(fun serviceProvider ->
        let appRepository = serviceProvider.GetRequiredService<IAppRepository>()
        let folderRepository = serviceProvider.GetRequiredService<IFolderRepository>()
        let resourceRepository = serviceProvider.GetRequiredService<IResourceRepository>()
        TrashHandler(appRepository, folderRepository, resourceRepository))
    |> ignore

    builder.Services.AddScoped<ICommandHandler<TrashCommand, TrashCommandResult>>(fun serviceProvider ->
        let trashHandler = serviceProvider.GetRequiredService<TrashHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "trash" trashHandler activitySource)
    |> ignore

    // Configure OpenTelemetry
    let activitySource = new ActivitySource("Freetool.Api")
    builder.Services.AddSingleton<ActivitySource>(activitySource) |> ignore

    builder.Services
        .AddOpenTelemetry()
        .WithTracing(fun tracing ->
            tracing
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("freetool-api", "1.0.0"))
                .AddSource("Freetool.Api")
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(fun options ->
                    let endpoint = builder.Configuration[ConfigurationKeys.Environment.OtlpEndpoint]

                    if not (System.String.IsNullOrEmpty(endpoint)) then
                        options.Endpoint <- System.Uri(endpoint)
                        options.Protocol <- OtlpExportProtocol.Grpc
                    else
                        startupLogger.LogInformation("No OTLP endpoint configured, using default"))
            |> ignore)
    |> ignore

    let app = builder.Build()

    // Debug logging for paths
    startupLogger.LogInformation("Content root: {ContentRoot}", builder.Environment.ContentRootPath)
    startupLogger.LogInformation("Web root: {WebRoot}", builder.Environment.WebRootPath)
    startupLogger.LogInformation("Current directory: {CurrentDirectory}", System.IO.Directory.GetCurrentDirectory())

    // Note: Database migrations were already run at startup (before OpenFGA store check)

    // Initialize OpenFGA authorization model if we have a valid store
    // Note: actualStoreId was set during DI registration (store was created if needed)
    if not (System.String.IsNullOrEmpty(actualStoreId)) then
        try
            startupLogger.LogInformation("Initializing OpenFGA authorization model...")
            use scope = app.Services.CreateScope()
            let authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            let modelTask = authService.WriteAuthorizationModelAsync()
            modelTask.Wait()
            startupLogger.LogInformation("OpenFGA authorization model initialized successfully")

            // Set up organization relations for all existing spaces
            // This ensures org admins inherit permissions on all spaces
            try
                startupLogger.LogInformation("Setting up organization relations for existing spaces...")

                let spaceRepository = scope.ServiceProvider.GetRequiredService<ISpaceRepository>()

                let spacesTask = spaceRepository.GetAllAsync 0 1000
                spacesTask.Wait()
                let spaces = spacesTask.Result

                for space in spaces do
                    let spaceId = Space.getId space
                    let spaceIdStr = spaceId.Value.ToString()

                    let tuple = {
                        Subject = Organization "default"
                        Relation = SpaceOrganization
                        Object = SpaceObject spaceIdStr
                    }

                    let relationTask = authService.CreateRelationshipsAsync([ tuple ])
                    relationTask.Wait()

                startupLogger.LogInformation("Organization relations set up for {Count} spaces", List.length spaces)
            with ex ->
                startupLogger.LogWarning(
                    "Could not set up organization relations for spaces: {Error}. Org admins may not have permissions on existing spaces.",
                    ex.Message
                )

            OpenFgaSpaceAuthorizationRepairStartup.runOpenFgaSpaceAuthorizationRepair startupLogger (fun () ->
                let repairService =
                    scope.ServiceProvider.GetRequiredService<IOpenFgaSpaceAuthorizationRepairService>()

                repairService.RepairAsync true None |> Async.AwaitTask |> Async.RunSynchronously)

            // Note: Organization admin is now set automatically when the user first logs in
            // via IapAuthMiddleware if their email matches OpenFGA:OrgAdminEmail config
            let orgAdminEmail = builder.Configuration[ConfigurationKeys.OpenFGA.OrgAdminEmail]

            if not (System.String.IsNullOrEmpty(orgAdminEmail)) then
                startupLogger.LogInformation(
                    "Organization admin email configured: {Email} (will be set when user first logs in)",
                    orgAdminEmail
                )

            // Run dev seeding after OpenFGA is initialized (only in dev mode)
            if isDevMode then
                try
                    let userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>()
                    let spaceRepository = scope.ServiceProvider.GetRequiredService<ISpaceRepository>()

                    let resourceRepository =
                        scope.ServiceProvider.GetRequiredService<IResourceRepository>()

                    let folderRepository = scope.ServiceProvider.GetRequiredService<IFolderRepository>()
                    let appRepository = scope.ServiceProvider.GetRequiredService<IAppRepository>()

                    // First, seed database data if needed (only runs if database is empty)
                    let seedTask =
                        DevSeedingService.seedDataAsync
                            startupLogger
                            userRepository
                            spaceRepository
                            resourceRepository
                            folderRepository
                            appRepository
                            authService

                    seedTask |> Async.AwaitTask |> Async.RunSynchronously

                    // Then, ensure OpenFGA relationships exist (always runs in dev mode)
                    // This handles the case where OpenFGA store was recreated but database still has users
                    let ensureRelationshipsTask =
                        DevSeedingService.ensureOpenFgaRelationshipsAsync
                            startupLogger
                            userRepository
                            spaceRepository
                            authService

                    ensureRelationshipsTask |> Async.AwaitTask |> Async.RunSynchronously
                with ex ->
                    startupLogger.LogWarning("[DEV MODE] Failed to seed dev data: {Error}", ex.Message)
        with ex ->
            startupLogger.LogWarning(
                "Could not initialize OpenFGA authorization model: {Error}. The application will continue, but authorization checks may fail. You can manually initialize by calling POST /admin/openfga/write-model",
                ex.Message
            )

    app.UseHttpsRedirection() |> ignore

    // Enable CORS for dev mode
    if isDevMode then
        app.UseCors("DevCors") |> ignore

    let provider = FileExtensionContentTypeProvider()
    provider.Mappings[".js"] <- "application/javascript"
    let staticFileOptions = StaticFileOptions()
    staticFileOptions.ContentTypeProvider <- provider
    app.UseStaticFiles(staticFileOptions) |> ignore

    // Use DevAuthMiddleware in dev mode; production always authenticates via Google IAP.
    if isDevMode then
        app.UseMiddleware<DevAuthMiddleware>() |> ignore
    else
        app.UseMiddleware<IapAuthMiddleware>() |> ignore

    app.UseSwagger(fun options -> options.RouteTemplate <- "openapi/{documentName}.json")
    |> ignore

    app.UseSwaggerUI(fun options ->
        options.RoutePrefix <- "openapi"
        options.SwaggerEndpoint("/openapi/v1.json", "freetool v1"))
    |> ignore

    app.MapGet("/healthy", Func<IResult>(fun () -> Results.Ok())) |> ignore

    app.MapGet(
        "/__api/me",
        Func<HttpContext, IResult>(fun context ->
            let requestUser = RequestUserContext.get context

            Results.Json(
                {|
                    userId = requestUser.UserId |> Option.map (fun userId -> userId.Value.ToString())
                    email = requestUser.Email
                    name = requestUser.Name
                    profile = requestUser.Profile
                    groupKeys = requestUser.GroupKeys
                    authenticationSource = requestUser.AuthenticationSource
                |}
            ))
    )
    |> ignore

    app.MapControllers() |> ignore

    app.MapFallbackToFile("index.html") |> ignore

    app.Run()
    0