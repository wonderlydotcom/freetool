module Freetool.Domain.Tests.AppTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

// Helper function for test assertions
let unwrapResult result =
    match result with
    | Ok value -> value
    | Error error -> failwith $"Expected Ok but got Error: {error}"

// Helper function to create apps for testing (uses empty resource to avoid conflicts)
let createAppForTesting actorUserId name folderId httpMethod inputs urlPath urlParameters headers body =
    // Create a dummy resource with no parameters to avoid conflicts
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let emptyResource =
        Resource.create actorUserId spaceId "Test Resource" "Test" "https://test.com" [] [] []
        |> unwrapResult

    App.create actorUserId name folderId emptyResource httpMethod inputs urlPath urlParameters headers body false None

[<Fact>]
let ``App creation should generate AppCreatedEvent`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "Email"
            Description = None
            Type = InputType.Email()
            Required = true
            DefaultValue = None
        }
        {
            Title = "Password"
            Description = None
            Type = InputType.Text(50) |> Result.defaultValue (InputType.Email())
            Required = true
            DefaultValue = None
        }
    ]

    // Act
    let resourceId = ResourceId.NewId()

    let result =
        createAppForTesting actorUserId "User Management App" folderId HttpMethod.Get inputs None [] [] []

    // Assert
    match result with
    | Ok app ->
        let events = App.getUncommittedEvents app
        Assert.Single(events) |> ignore

        match events.[0] with
        | :? AppCreatedEvent as event ->
            Assert.Equal("User Management App", event.Name.Value)
            Assert.Equal(Some folderId, event.FolderId)
            Assert.Equal(2, event.Inputs.Length)
            Assert.Equal("Email", event.Inputs.[0].Title)
            Assert.Equal("Password", event.Inputs.[1].Title)
        | _ -> Assert.True(false, "Expected AppCreatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App name update should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Old App Name" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    // Act
    let result = App.updateName actorUserId "New App Name" app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.NameChanged(oldValue, newValue) ->
                Assert.Equal("Old App Name", oldValue.Value)
                Assert.Equal("New App Name", newValue.Value)
            | _ -> Assert.True(false, "Expected NameChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App inputs update should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let initialInputs = [
        {
            Title = "Name"
            Description = None
            Type = InputType.Text(100) |> Result.defaultValue (InputType.Email())
            Required = true
            DefaultValue = None
        }
    ]

    let resourceId = ResourceId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get initialInputs None [] [] []
        |> unwrapResult

    let newInputs = [
        {
            Title = "First Name"
            Description = None
            Type = InputType.Text(50) |> Result.defaultValue (InputType.Email())
            Required = true
            DefaultValue = None
        }
        {
            Title = "Last Name"
            Description = None
            Type = InputType.Text(50) |> Result.defaultValue (InputType.Email())
            Required = false
            DefaultValue = None
        }
        {
            Title = "Age"
            Description = None
            Type = InputType.Integer()
            Required = true
            DefaultValue = None
        }
    ]

    // Act
    let result = App.updateInputs actorUserId newInputs app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.InputsChanged(oldInputs, newInputs) ->
                Assert.Single(oldInputs) |> ignore
                Assert.Equal("Name", oldInputs.[0].Title)
                Assert.Equal(3, newInputs.Length)
                Assert.Equal("First Name", newInputs.[0].Title)
                Assert.Equal("Last Name", newInputs.[1].Title)
                Assert.Equal("Age", newInputs.[2].Title)
            | _ -> Assert.True(false, "Expected InputsChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App creation should reject empty name`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    // Act
    let resourceId = ResourceId.NewId()

    let result =
        createAppForTesting actorUserId "" folderId HttpMethod.Get [] None [] [] []

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("App name cannot be empty", msg)
    | _ -> Assert.True(false, "Expected validation error for empty name")

[<Fact>]
let ``App creation should reject name longer than 100 characters`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()
    let longName = String.replicate 101 "a"

    // Act
    let resourceId = ResourceId.NewId()

    let result =
        createAppForTesting actorUserId longName folderId HttpMethod.Get [] None [] [] []

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("App name cannot exceed 100 characters", msg)
    | _ -> Assert.True(false, "Expected validation error for long name")

[<Fact>]
let ``App deletion should generate AppDeletedEvent`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    // Act
    let deletedApp = App.markForDeletion actorUserId app

    // Assert
    let events = App.getUncommittedEvents deletedApp
    Assert.Equal(2, events.Length) // Creation event + deletion event

    match events.[1] with // The deletion event is the second one
    | :? AppDeletedEvent as event -> Assert.Equal(app.State.Id, event.AppId)
    | _ -> Assert.True(false, "Expected AppDeletedEvent")

[<Fact>]
let ``App validation should reject invalid input title`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let invalidInputs = [
        {
            Title = ""
            Description = None
            Type = InputType.Email()
            Required = true
            DefaultValue = None
        }
    ] // Empty title

    // Act
    let resourceId = ResourceId.NewId()

    let result =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get invalidInputs None [] [] []

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("Input title cannot be empty", msg)
    | _ -> Assert.True(false, "Expected validation error for invalid input title")

[<Fact>]
let ``App creation with URL parameters should work correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()
    let urlParameters = [ "page", "1"; "size", "10" ]

    // Act
    let result =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None urlParameters [] []

    // Assert
    match result with
    | Ok app ->
        let retrievedParams = App.getUrlParameters app
        Assert.Equal(2, retrievedParams.Length)
        Assert.Contains(("page", "1"), retrievedParams)
        Assert.Contains(("size", "10"), retrievedParams)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App creation with headers should work correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let headers = [ "Authorization", "Bearer token123"; "Content-Type", "application/json" ]

    // Act
    let result =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] headers []

    // Assert
    match result with
    | Ok app ->
        let retrievedHeaders = App.getHeaders app
        Assert.Equal(2, retrievedHeaders.Length)
        Assert.Contains(("Authorization", "Bearer token123"), retrievedHeaders)
        Assert.Contains(("Content-Type", "application/json"), retrievedHeaders)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App creation with body should work correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()
    let body = [ "username", "john.doe"; "email", "john@example.com" ]

    // Act
    let result =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] body

    // Assert
    match result with
    | Ok app ->
        let retrievedBody = App.getBody app
        Assert.Equal(2, retrievedBody.Length)
        Assert.Contains(("username", "john.doe"), retrievedBody)
        Assert.Contains(("email", "john@example.com"), retrievedBody)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App creation with URL path should work correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()
    let urlPath = Some "/users/profile"

    // Act
    let result =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] urlPath [] [] []

    // Assert
    match result with
    | Ok app ->
        let retrievedPath = App.getUrlPath app
        Assert.Equal(Some "/users/profile", retrievedPath)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App update URL parameters should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    let newUrlParams = [ "filter", "active"; "sort", "name" ]

    // Act
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let emptyResource =
        Resource.create actorUserId spaceId "Test Resource" "Test" "https://test.com" [] [] []
        |> unwrapResult

    let resourceConflictData = Resource.toConflictData emptyResource

    let result =
        App.updateUrlParameters actorUserId newUrlParams resourceConflictData app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.UrlParametersChanged(oldParams, newParams) ->
                Assert.Empty(oldParams) // Should be empty initially
                Assert.Equal(2, newParams.Length)
            | _ -> Assert.True(false, "Expected UrlParametersChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App update headers should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    let newHeaders = [ ("X-API-Key", "secret123") ]

    // Act
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let emptyResource =
        Resource.create actorUserId spaceId "Test Resource" "Test" "https://test.com" [] [] []
        |> unwrapResult

    let resourceConflictData = Resource.toConflictData emptyResource

    let result = App.updateHeaders actorUserId newHeaders resourceConflictData app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.HeadersChanged(oldHeaders, newHeaders) ->
                Assert.Empty(oldHeaders) // Should be empty initially
                Assert.Single(newHeaders) |> ignore
            | _ -> Assert.True(false, "Expected HeadersChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App update body should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    let newBody = [ ("data", "test") ]

    // Act
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let emptyResource =
        Resource.create actorUserId spaceId "Test Resource" "Test" "https://test.com" [] [] []
        |> unwrapResult

    let resourceConflictData = Resource.toConflictData emptyResource

    let result = App.updateBody actorUserId newBody resourceConflictData app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.BodyChanged(oldBody, newBody) ->
                Assert.Empty(oldBody) // Should be empty initially
                Assert.Single(newBody) |> ignore
            | _ -> Assert.True(false, "Expected BodyChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App update URL path should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let app =
        createAppForTesting actorUserId "Test App" folderId HttpMethod.Get [] None [] [] []
        |> unwrapResult

    let newUrlPath = Some "/api/v2/users"

    // Act
    let result = App.updateUrlPath actorUserId newUrlPath app

    // Assert
    match result with
    | Ok updatedApp ->
        let events = App.getUncommittedEvents updatedApp
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? AppUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | AppChange.UrlPathChanged(oldPath, newPath) ->
                Assert.Equal(None, oldPath) // Should be None initially
                Assert.Equal(Some "/api/v2/users", newPath)
            | _ -> Assert.True(false, "Expected UrlPathChanged event")
        | _ -> Assert.True(false, "Expected AppUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``App create should reject URL parameter conflicts`` () =
    // Arrange - Resource with URL parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create
            actorUserId
            spaceId
            "API"
            "Test API"
            "https://api.test.com"
            [ "version", "v1"; "format", "json" ] [] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Try to create App with conflicting URL parameter "format"
    let result =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Get
            []
            None
            [ ("format", "xml"); ("new_param", "value") ]
            []
            []
            false
            None

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("URL parameters: format", msg)
    | Ok _ -> Assert.True(false, "Expected error for URL parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App create should reject header conflicts`` () =
    // Arrange - Resource with headers
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [
            "Content-Type", "application/json"
            "Accept", "application/json"
        ] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Try to create App with conflicting header "Content-Type"
    let result =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Post
            []
            None
            []
            [ "Content-Type", "application/xml"; "Authorization", "Bearer token" ]
            []
            false
            None

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Headers: Content-Type", msg)
    | Ok _ -> Assert.True(false, "Expected error for header conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App create should reject body parameter conflicts`` () =
    // Arrange - Resource with body parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [] [
            "client_id", "12345"
            "scope", "read"
        ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Try to create App with conflicting body parameter "client_id"
    let result =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Put
            []
            None
            []
            []
            [ "client_id", "override"; "new_param", "value" ]
            false
            None

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Body parameters: client_id", msg)
    | Ok _ -> Assert.True(false, "Expected error for body parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App create should reject multiple conflicts`` () =
    // Arrange - Resource with parameters in all categories
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ "version", "v1" ] [
            "Content-Type", "application/json"
        ] [ "client_id", "12345" ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Try to create App with conflicts in all categories
    let result =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Post
            []
            None
            [ ("version", "v2") ]
            [ ("Content-Type", "application/xml") ]
            [ ("client_id", "override") ]
            false
            None

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("URL parameters: version", msg)
        Assert.Contains("Headers: Content-Type", msg)
        Assert.Contains("Body parameters: client_id", msg)
    | Ok _ -> Assert.True(false, "Expected error for multiple conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App create should allow extending with no conflicts`` () =
    // Arrange - Resource with some parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ "version", "v1" ] [
            "Content-Type", "application/json"
        ] [ "client_id", "12345" ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Create App with only new values (no conflicts)
    let result =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Get
            []
            None
            [ "page", "1"; "size", "10" ]
            [ ("Authorization", "Bearer token") ]
            [ ("include_metadata", "true") ]
            false
            None

    // Assert
    match result with
    | Ok app ->
        // Verify the app was created successfully
        Assert.Equal("Test App", App.getName app)
        Assert.Equal(folderId, App.getFolderId app)
        Assert.Equal(Resource.getId resource, App.getResourceId app)

        // Verify the parameters were set correctly
        let urlParams = App.getUrlParameters app
        Assert.Equal(2, urlParams.Length)
        Assert.Contains(("page", "1"), urlParams)
        Assert.Contains(("size", "10"), urlParams)

        let headers = App.getHeaders app
        Assert.Single(headers) |> ignore
        Assert.Contains(("Authorization", "Bearer token"), headers)

        let body = App.getBody app
        Assert.Single(body) |> ignore
        Assert.Contains(("include_metadata", "true"), body)

    | Error error -> Assert.True(false, $"Expected successful creation but got error: {error}")

[<Fact>]
let ``App create should allow empty app parameters`` () =
    // Arrange - Resource with some parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ "version", "v1" ] [
            "Content-Type", "application/json"
        ] [ "client_id", "12345" ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Act - Create App with no additional parameters
    let result =
        App.create actorUserId "Test App" folderId resource HttpMethod.Delete [] None [] [] [] false None

    // Assert
    match result with
    | Ok app ->
        Assert.Equal("Test App", App.getName app)
        Assert.Equal(folderId, App.getFolderId app)
        Assert.Equal(Resource.getId resource, App.getResourceId app)

        // App should have no additional parameters
        Assert.Empty(App.getUrlParameters app)
        Assert.Empty(App.getHeaders app)
        Assert.Empty(App.getBody app)

    | Error error -> Assert.True(false, $"Expected successful creation but got error: {error}")

[<Fact>]
let ``App updateUrlParameters should reject resource parameter conflicts`` () =
    // Arrange - Resource with URL parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create
            actorUserId
            spaceId
            "API"
            "Test API"
            "https://api.test.com"
            [ "version", "v1"; "format", "json" ] [] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get [] None [] [] [] false None
        |> unwrapResult

    // Act - Try to update with conflicting URL parameter "format"
    let result =
        let resourceConflictData = Resource.toConflictData resource

        App.updateUrlParameters actorUserId [ "format", "xml"; "new_param", "value" ] resourceConflictData app

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("URL parameters: format", msg)
    | Ok _ -> Assert.True(false, "Expected error for URL parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App updateHeaders should reject resource header conflicts`` () =
    // Arrange - Resource with headers
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [
            "Content-Type", "application/json"
            "Accept", "application/json"
        ] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Post [] None [] [] [] false None
        |> unwrapResult

    // Act - Try to update with conflicting header "Content-Type"
    let result =
        let resourceConflictData = Resource.toConflictData resource

        App.updateHeaders
            actorUserId
            [ "Content-Type", "application/xml"; "Authorization", "Bearer token" ]
            resourceConflictData
            app

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Headers: Content-Type", msg)
    | Ok _ -> Assert.True(false, "Expected error for header conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App updateBody should reject resource body parameter conflicts`` () =
    // Arrange - Resource with body parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [] [
            "client_id", "12345"
            "scope", "read"
        ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Put [] None [] [] [] false None
        |> unwrapResult

    // Act - Try to update with conflicting body parameter "client_id"
    let result =
        let resourceConflictData = Resource.toConflictData resource

        App.updateBody actorUserId [ "client_id", "override"; "new_param", "value" ] resourceConflictData app

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Body parameters: client_id", msg)
    | Ok _ -> Assert.True(false, "Expected error for body parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``App updateUrlParameters should allow new parameters with no conflicts`` () =
    // Arrange - Resource with some URL parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ "version", "v1" ] [] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get [] None [] [] [] false None
        |> unwrapResult

    // Act - Update with only new parameters (no conflicts)
    let resourceConflictData = Resource.toConflictData resource

    let result =
        App.updateUrlParameters actorUserId [ "page", "1"; "size", "10" ] resourceConflictData app

    // Assert
    match result with
    | Ok updatedApp ->
        let urlParams = App.getUrlParameters updatedApp
        Assert.Equal(2, urlParams.Length)
        Assert.Contains(("page", "1"), urlParams)
        Assert.Contains(("size", "10"), urlParams)
    | Error error -> Assert.True(false, $"Expected successful update but got error: {error}")

[<Fact>]
let ``App updateHeaders should allow new headers with no conflicts`` () =
    // Arrange - Resource with some headers
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [
            "Content-Type", "application/json"
        ] []

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Post [] None [] [] [] false None
        |> unwrapResult

    // Act - Update with only new headers (no conflicts)
    let result =
        let resourceConflictData = Resource.toConflictData resource

        App.updateHeaders
            actorUserId
            [ "Authorization", "Bearer token"; "X-API-Key", "secret" ]
            resourceConflictData
            app

    // Assert
    match result with
    | Ok updatedApp ->
        let headers = App.getHeaders updatedApp
        Assert.Equal(2, headers.Length)
        Assert.Contains(("Authorization", "Bearer token"), headers)
        Assert.Contains(("X-API-Key", "secret"), headers)
    | Error error -> Assert.True(false, $"Expected successful update but got error: {error}")

[<Fact>]
let ``App updateBody should allow new body parameters with no conflicts`` () =
    // Arrange - Resource with some body parameters
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [] [ "client_id", "12345" ]

    let resource = unwrapResult resourceResult
    let folderId = FolderId.NewId()

    // Create app with no conflicts initially
    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Put [] None [] [] [] false None
        |> unwrapResult

    // Act - Update with only new body parameters (no conflicts)
    let result =
        let resourceConflictData = Resource.toConflictData resource

        App.updateBody actorUserId [ "include_metadata", "true"; "format", "detailed" ] resourceConflictData app

    // Assert
    match result with
    | Ok updatedApp ->
        let body = App.getBody updatedApp
        Assert.Equal(2, body.Length)
        Assert.Contains(("include_metadata", "true"), body)
        Assert.Contains(("format", "detailed"), body)
    | Error error -> Assert.True(false, $"Expected successful update but got error: {error}")