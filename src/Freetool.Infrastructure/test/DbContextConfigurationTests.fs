module Freetool.Infrastructure.Tests.DbContextConfigurationTests

open System
open System.Linq
open Xunit
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Freetool.Infrastructure.Database
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects

[<Fact>]
let ``DbContext can be created and configured without errors`` () =
    // Arrange
    let serviceCollection = ServiceCollection()

    serviceCollection.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase("TestDb_Configuration") |> ignore)
    |> ignore

    let serviceProvider = serviceCollection.BuildServiceProvider()

    // Act & Assert - This will throw if entity configuration is invalid
    use context = serviceProvider.GetRequiredService<FreetoolDbContext>()
    let model = context.Model
    Assert.NotNull(model)

[<Fact>]
let ``All entity types can be queried without constructor binding errors`` () =
    // Arrange
    let serviceCollection = ServiceCollection()

    serviceCollection.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase("TestDb_Instantiation") |> ignore)
    |> ignore

    let serviceProvider = serviceCollection.BuildServiceProvider()
    use context = serviceProvider.GetRequiredService<FreetoolDbContext>()

    // Act & Assert - Test each entity type that had constructor binding issues
    // These operations will fail if EF can't instantiate the entity types

    try
        let _ = context.Apps.Where(fun a -> a.Id = AppId.NewId()).ToList()
        () // Success
    with ex ->
        Assert.True(false, $"AppData constructor binding failed: {ex.Message}")

    try
        let _ = context.Resources.Where(fun r -> r.Id = ResourceId.NewId()).ToList()
        () // Success
    with ex ->
        Assert.True(false, $"ResourceData constructor binding failed: {ex.Message}")

    try
        let _ = context.Users.Where(fun u -> u.Id = UserId.NewId()).ToList()
        () // Success
    with ex ->
        Assert.True(false, $"UserData constructor binding failed: {ex.Message}")

[<Fact>]
let ``AppData with complex JSON properties can be persisted and retrieved`` () =
    // Arrange
    let serviceCollection = ServiceCollection()

    serviceCollection.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase($"TestDb_AppData_{Guid.NewGuid()}") |> ignore)
    |> ignore

    let serviceProvider = serviceCollection.BuildServiceProvider()
    use context = serviceProvider.GetRequiredService<FreetoolDbContext>()

    // Create test data with complex properties that need JSON serialization
    let testInputType =
        match InputType.Text(100) with
        | Ok inputType -> inputType
        | Error _ -> failwith "Failed to create test InputType"

    let testInput: Freetool.Domain.Events.Input = {
        Title = "Test Input"
        Description = None
        Type = testInputType
        Required = true
        DefaultValue = None
    }

    let currencyInput: Freetool.Domain.Events.Input = {
        Title = "Budget"
        Description = Some "USD currency field"
        Type = InputType.Currency(SupportedCurrency.USD)
        Required = false
        DefaultValue = None
    }

    let testKeyValuePair =
        match KeyValuePair.Create("testKey", "testValue") with
        | Ok kvp -> kvp
        | Error _ -> failwith "Failed to create test KeyValuePair"

    let appData: AppData = {
        Id = AppId.NewId()
        Name = "Test App"
        FolderId = FolderId.NewId()
        ResourceId = ResourceId.NewId()
        HttpMethod = HttpMethod.Get
        Inputs = [ testInput; currencyInput ]
        UrlPath = Some "/test"
        UrlParameters = [ testKeyValuePair ]
        Headers = [ testKeyValuePair ]
        Body = [ testKeyValuePair ]
        UseDynamicJsonBody = false
        SqlConfig = None
        Description = None
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    // Act - This will fail if JSON serialization doesn't work
    context.Apps.Add(appData) |> ignore
    let saveResult = context.SaveChanges()

    // Clear context to ensure we're reading from database
    context.ChangeTracker.Clear()

    let retrievedApps = context.Apps.Where(fun a -> a.Id = appData.Id).ToList()

    // Assert
    Assert.True(saveResult > 0, "Failed to save AppData")
    Assert.Single(retrievedApps) |> ignore
    let retrievedApp = retrievedApps.First()
    Assert.Equal(appData.Name, retrievedApp.Name)
    Assert.Equal(appData.Inputs.Length, retrievedApp.Inputs.Length)
    Assert.Equal(testInput.Title, retrievedApp.Inputs.[0].Title)
    Assert.Equal(currencyInput.Title, retrievedApp.Inputs.[1].Title)

    match retrievedApp.Inputs.[1].Type.Value with
    | InputTypeValue.Currency SupportedCurrency.USD -> Assert.True(true)
    | _ -> Assert.True(false, "Currency input type did not deserialize correctly")

[<Fact>]
let ``ResourceData with complex JSON properties can be persisted and retrieved`` () =
    // Arrange
    let serviceCollection = ServiceCollection()

    serviceCollection.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase($"TestDb_ResourceData_{Guid.NewGuid()}") |> ignore)
    |> ignore

    let serviceProvider = serviceCollection.BuildServiceProvider()
    use context = serviceProvider.GetRequiredService<FreetoolDbContext>()

    let testKeyValuePair =
        match KeyValuePair.Create("testKey", "testValue") with
        | Ok kvp -> kvp
        | Error _ -> failwith "Failed to create test KeyValuePair"

    let resourceName =
        match ResourceName.Create(Some "Test Resource") with
        | Ok name -> name
        | Error _ -> failwith "Failed to create ResourceName"

    let resourceDescription =
        match ResourceDescription.Create(Some "Test Description") with
        | Ok desc -> desc
        | Error _ -> failwith "Failed to create ResourceDescription"

    let baseUrl =
        match BaseUrl.Create(Some "https://api.test.com") with
        | Ok url -> url
        | Error _ -> failwith "Failed to create BaseUrl"

    let resourceData: ResourceData = {
        Id = ResourceId.NewId()
        SpaceId = SpaceId.FromGuid(Guid.NewGuid())
        Name = resourceName
        Description = resourceDescription
        ResourceKind = ResourceKind.Http
        BaseUrl = Some baseUrl
        UrlParameters = [ testKeyValuePair ]
        Headers = [ testKeyValuePair ]
        Body = [ testKeyValuePair ]
        DatabaseName = None
        DatabaseHost = None
        DatabasePort = None
        DatabaseEngine = None
        DatabaseAuthScheme = None
        DatabaseUsername = None
        DatabasePassword = None
        UseSsl = false
        EnableSshTunnel = false
        ConnectionOptions = []
        CreatedAt = DateTime.UtcNow
        UpdatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    // Act - This will fail if JSON serialization doesn't work
    context.Resources.Add(resourceData) |> ignore
    let saveResult = context.SaveChanges()

    context.ChangeTracker.Clear()

    let retrievedResources =
        context.Resources.Where(fun r -> r.Id = resourceData.Id).ToList()

    // Assert
    Assert.True(saveResult > 0, "Failed to save ResourceData")
    Assert.Single(retrievedResources) |> ignore
    let retrievedResource = retrievedResources.First()
    Assert.Equal(resourceData.Name.Value, retrievedResource.Name.Value)
    Assert.Equal(resourceData.UrlParameters.Length, retrievedResource.UrlParameters.Length)

[<Fact>]
let ``Entity Framework model validation passes for all configured entities`` () =
    // Arrange
    let serviceCollection = ServiceCollection()

    serviceCollection.AddDbContext<FreetoolDbContext>(fun options ->
        options.UseInMemoryDatabase("TestDb_Validation") |> ignore)
    |> ignore

    let serviceProvider = serviceCollection.BuildServiceProvider()
    use context = serviceProvider.GetRequiredService<FreetoolDbContext>()

    // Act & Assert - This will throw if there are any model configuration issues
    let model = context.Model
    let entityTypes = model.GetEntityTypes()

    // Verify we have all expected entity types
    let entityTypeNames =
        entityTypes |> Seq.map (fun et -> et.ClrType.Name) |> Set.ofSeq

    let expectedTypes =
        Set.ofList [
            "AppData"
            "ResourceData"
            "UserData"
            "FolderData"
            "EventData"
            "RunData"
            "SpaceData"
            "SpaceMemberData"
        ]

    Assert.True(
        expectedTypes.IsSubsetOf(entityTypeNames),
        $"Missing entity types: {Set.difference expectedTypes entityTypeNames}"
    )

    // Verify each entity type has properly configured properties
    for entityType in entityTypes do
        let properties = entityType.GetProperties()
        Assert.True((properties |> Seq.length) > 0, $"Entity {entityType.ClrType.Name} has no properties")

        // Verify primary key is configured
        let primaryKey = entityType.FindPrimaryKey()
        Assert.NotNull(primaryKey)