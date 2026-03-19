module Freetool.Domain.Tests.RequestComposerTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Domain.Services

// Helper function for test assertions
let unwrapResult result =
    match result with
    | Ok value -> value
    | Error error -> failwith $"Expected Ok but got Error: {error}"

[<Fact>]
let ``RequestComposer should combine Resource and App correctly`` () =
    // Arrange - Create a Resource
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create
            actorUserId
            spaceId
            "User API"
            "API for user management"
            "https://api.example.com"
            [ ("version", "v1") ] [ ("Content-Type", "application/json") ] [ ("client_id", "12345") ]

    let resource = unwrapResult resourceResult
    let resourceId = Resource.getId resource

    // Create an App that references this Resource
    let folderId = FolderId.NewId()

    let appResult =
        App.create
            actorUserId
            "User Profile App"
            folderId
            resource
            HttpMethod.Get
            []
            (Some "/users/profile")
            [ "page", "1"; "size", "10" ]
            [ ("Authorization", "Bearer token123") ]
            [ ("include_metadata", "true") ]
            false
            None

    let app = unwrapResult appResult

    // Act - Compose them into an ExecutableHttpRequest
    let compositionResult = RequestComposer.composeExecutableRequest resource app

    // Assert
    match compositionResult with
    | Ok executableRequest ->
        // Check base URL composition
        Assert.Equal("https://api.example.com/users/profile", executableRequest.BaseUrl)

        // Check HTTP method comes from App
        Assert.Equal("GET", executableRequest.HttpMethod)

        // Check URL parameters (Resource + App - all new App parameters)
        Assert.Equal(3, executableRequest.UrlParameters.Length)
        Assert.Contains(("version", "v1"), executableRequest.UrlParameters) // From Resource
        Assert.Contains(("page", "1"), executableRequest.UrlParameters) // From App (new)
        Assert.Contains(("size", "10"), executableRequest.UrlParameters) // From App (new)

        // Check headers (Resource + App - all new App headers)
        Assert.Equal(2, executableRequest.Headers.Length)
        Assert.Contains(("Content-Type", "application/json"), executableRequest.Headers) // From Resource
        Assert.Contains(("Authorization", "Bearer token123"), executableRequest.Headers) // From App (new)

        // Check body (Resource + App - all new App body parameters)
        Assert.Equal(2, executableRequest.Body.Length)
        Assert.Contains(("client_id", "12345"), executableRequest.Body) // From Resource
        Assert.Contains(("include_metadata", "true"), executableRequest.Body) // From App (new)

    | Error error -> Assert.True(false, $"Expected successful composition but got error: {error}")

[<Fact>]
let ``RequestComposer should handle URL path composition correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    // Test different URL path scenarios
    let testCases = [
        // (Resource BaseUrl, App UrlPath, Expected Result)
        "https://api.test.com", Some "/users", "https://api.test.com/users"
        "https://api.test.com/", Some "/users", "https://api.test.com/users"
        "https://api.test.com", Some "users", "https://api.test.com/users"
        "https://api.test.com/", Some "users", "https://api.test.com/users"
        "https://api.test.com/v1", Some "/users", "https://api.test.com/v1/users"
        "https://api.test.com", None, "https://api.test.com"
        "https://api.test.com/", None, "https://api.test.com/"
    ]

    for baseUrl, urlPath, expected in testCases do
        // Create Resource with specific base URL
        let spaceId = SpaceId.FromGuid(Guid.NewGuid())

        let resourceWithUrl =
            Resource.create actorUserId spaceId "API" "Test" baseUrl [] [] []
            |> unwrapResult

        // Create App with specific URL path using the resource
        let appResult =
            App.create actorUserId "Test App" folderId resourceWithUrl HttpMethod.Get [] urlPath [] [] [] false None

        let app = unwrapResult appResult

        // Act
        let compositionResult = RequestComposer.composeExecutableRequest resourceWithUrl app

        // Assert
        match compositionResult with
        | Ok executableRequest -> Assert.Equal(expected, executableRequest.BaseUrl)
        | Error error -> Assert.True(false, $"Expected success for {baseUrl} + {urlPath} but got error: {error}")

[<Fact>]
let ``RequestComposer should allow App extending Resource with new values`` () =
    // Arrange - Resource with some parameters, headers, and body
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ "version", "v1" ] [
            "Content-Type", "application/json"
        ] [ "client_id", "12345" ]

    let resource = unwrapResult resourceResult
    let resourceId = Resource.getId resource
    let folderId = FolderId.NewId()

    // App that only adds new values (no overrides)
    let appResult =
        App.create
            actorUserId
            "Extend App"
            folderId
            resource
            HttpMethod.Put
            []
            None
            [ ("page", "1"); ("size", "10") ]
            [ "Authorization", "Bearer xyz" ]
            [ "include_metadata", "true" ]
            false
            None

    let app = unwrapResult appResult

    // Act
    let compositionResult = RequestComposer.composeExecutableRequest resource app

    // Assert
    match compositionResult with
    | Ok executableRequest ->
        // URL Parameters: Should contain both Resource and App parameters
        Assert.Contains(("version", "v1"), executableRequest.UrlParameters) // From Resource
        Assert.Contains(("page", "1"), executableRequest.UrlParameters) // From App (new)
        Assert.Contains(("size", "10"), executableRequest.UrlParameters) // From App (new)
        Assert.Equal(3, executableRequest.UrlParameters.Length)

        // Headers: Should contain both Resource and App headers
        Assert.Contains(("Content-Type", "application/json"), executableRequest.Headers) // From Resource
        Assert.Contains(("Authorization", "Bearer xyz"), executableRequest.Headers) // From App (new)
        Assert.Equal(2, executableRequest.Headers.Length)

        // Body: Should contain both Resource and App body parameters
        Assert.Contains(("client_id", "12345"), executableRequest.Body) // From Resource
        Assert.Contains(("include_metadata", "true"), executableRequest.Body) // From App (new)
        Assert.Equal(2, executableRequest.Body.Length)

    | Error error -> Assert.True(false, $"Expected successful composition but got error: {error}")

[<Fact>]
let ``RequestComposer should reject mismatched ResourceId`` () =
    // Arrange - Create a Resource
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [] [] []

    let resource = unwrapResult resourceResult

    // Create an App with a DIFFERENT Resource
    let folderId = FolderId.NewId()

    let differentResource =
        Resource.create actorUserId spaceId "Different API" "Test" "https://different.com" [] [] []
        |> unwrapResult

    let appResult =
        App.create actorUserId "Mismatched App" folderId differentResource HttpMethod.Get [] None [] [] [] false None

    let app = unwrapResult appResult

    // Act
    let compositionResult = RequestComposer.composeExecutableRequest resource app

    // Assert
    match compositionResult with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App references ResourceId", msg)
        Assert.Contains("but provided Resource has Id", msg)
    | Ok _ -> Assert.True(false, "Expected error for mismatched ResourceId")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``RequestComposer should handle empty App extensions`` () =
    // Arrange - Resource with some data
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let resourceResult =
        Resource.create actorUserId spaceId "API" "Test API" "https://api.test.com" [ ("version", "v1") ] [
            ("Content-Type", "application/json")
        ] [ ("client_id", "12345") ]

    let resource = unwrapResult resourceResult
    let resourceId = Resource.getId resource
    let folderId = FolderId.NewId()

    // App with no extensions (empty lists and None)
    let appResult =
        App.create actorUserId "Empty App" folderId resource HttpMethod.Delete [] None [] [] [] false None

    let app = unwrapResult appResult

    // Act
    let compositionResult = RequestComposer.composeExecutableRequest resource app

    // Assert
    match compositionResult with
    | Ok executableRequest ->
        // Should preserve all Resource values
        Assert.Equal("https://api.test.com", executableRequest.BaseUrl)
        Assert.Equal("DELETE", executableRequest.HttpMethod)
        Assert.Single(executableRequest.UrlParameters) |> ignore
        Assert.Equal(("version", "v1"), executableRequest.UrlParameters.[0])
        Assert.Single(executableRequest.Headers) |> ignore
        Assert.Equal(("Content-Type", "application/json"), executableRequest.Headers.[0])
        Assert.Single(executableRequest.Body) |> ignore
        Assert.Equal(("client_id", "12345"), executableRequest.Body.[0])

    | Error error -> Assert.True(false, $"Expected successful composition but got error: {error}")