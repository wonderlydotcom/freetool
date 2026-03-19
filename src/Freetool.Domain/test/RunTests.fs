module Freetool.Domain.Tests.RunTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events
open Freetool.Domain.Services

// Helper function for test assertions
let unwrapResult result =
    match result with
    | Ok value -> value
    | Error error -> failwith $"Expected Ok but got Error: {error}"

// Helper function to create test app with inputs - returns both app and resource for testing
let createTestAppWithResource () =
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs = [
        {
            Title = "userId"
            Description = None
            Type = InputType.Text(50) |> Result.defaultValue (InputType.Email())
            Required = true
            DefaultValue = None
        }
        {
            Title = "email"
            Description = None
            Type = InputType.Email()
            Required = true
            DefaultValue = None
        }
    ]

    // Create a test resource
    let resource =
        Resource.create
            actorUserId
            spaceId
            "Test API"
            "Test endpoint"
            "https://api.test.com/users/@userId"
            [ "limit", "10" ] [ "Authorization", "Bearer @token" ] [ "email", "@email" ]

        |> unwrapResult

    let app =
        App.create
            actorUserId
            "Test App"
            folderId
            resource
            HttpMethod.Get
            inputs
            (Some "/@userId/profile")
            []
            []
            []
            false
            None
        |> unwrapResult

    app, resource

// Helper function to create test app with inputs
let createTestApp () = createTestAppWithResource () |> fst

[<Fact>]
let ``Run creation should validate required inputs`` () =
    // Arrange
    let app = createTestApp ()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())

    let inputValues = [
        { Title = "userId"; Value = "123" }
    // Missing required email input intentionally
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("email", message)
    | _ -> Assert.True(false, "Expected validation error for missing required input")

[<Fact>]
let ``Run creation should reject invalid input names`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
        {
            Title = "invalidInput"
            Value = "test"
        }
    ] // This input doesn't exist in app schema

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("Invalid inputs not defined in app", message)
        Assert.Contains("invalidInput", message)
    | _ -> Assert.True(false, "Expected validation error for invalid input name")

[<Fact>]
let ``Run creation with valid inputs should succeed and generate events`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok run ->
        Assert.Equal(Pending, Run.getStatus run)
        Assert.Equal(App.getId app, Run.getAppId run)
        Assert.Equal<RunInputValue list>(inputValues, Run.getInputValues run)

        let events = Run.getUncommittedEvents run
        Assert.Single events

        match events.[0] with
        | :? RunCreatedEvent as event ->
            Assert.Equal(Run.getId run, event.RunId)
            Assert.Equal(App.getId app, event.AppId)
            Assert.Equal<RunInputValue list>(inputValues, event.InputValues)
        | _ -> Assert.True(false, "Expected RunCreatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run status transitions should generate correct events`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    // Act - transition to running
    let runningRun = Run.markAsRunning actorUserId run

    // Assert
    Assert.Equal(Running, Run.getStatus runningRun)
    Assert.True((Run.getStartedAt runningRun).IsSome)

    let events = Run.getUncommittedEvents runningRun

    let statusEvents =
        events
        |> List.choose (function
            | :? RunStatusChangedEvent as e -> Some e
            | _ -> None)

    Assert.Single statusEvents

    let statusEvent = statusEvents.[0]
    Assert.Equal(Pending, statusEvent.OldStatus)
    Assert.Equal(Running, statusEvent.NewStatus)

[<Fact>]
let ``Run completion with success should update status and response`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult
    let runningRun = Run.markAsRunning actorUserId run
    let response = """{"id": 123, "name": "John Doe"}"""

    // Act
    let completedRun = Run.markAsSuccess actorUserId response runningRun

    // Assert
    Assert.Equal(Success, Run.getStatus completedRun)
    Assert.Equal(Some response, Run.getResponse completedRun)
    Assert.True((Run.getCompletedAt completedRun).IsSome)
    Assert.Equal(None, Run.getErrorMessage completedRun)

[<Fact>]
let ``Run completion with failure should update status and error message`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult
    let runningRun = Run.markAsRunning actorUserId run
    let errorMessage = "Network timeout after 30 seconds"

    // Act
    let failedRun = Run.markAsFailure actorUserId errorMessage runningRun

    // Assert
    Assert.Equal(Failure, Run.getStatus failedRun)
    Assert.Equal(Some errorMessage, Run.getErrorMessage failedRun)
    Assert.True((Run.getCompletedAt failedRun).IsSome)
    Assert.Equal(None, Run.getResponse failedRun)

[<Fact>]
let ``Run with invalid configuration should update status correctly`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app = createTestApp ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult
    let configError = "Resource not found"

    // Act
    let invalidRun = Run.markAsInvalidConfiguration actorUserId configError run

    // Assert
    Assert.Equal(InvalidConfiguration, Run.getStatus invalidRun)
    Assert.Equal(Some configError, Run.getErrorMessage invalidRun)
    Assert.True((Run.getCompletedAt invalidRun).IsSome)

[<Fact>]
let ``Run executable request composition should substitute input values`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithResource ()

    let inputValues = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "john@example.com"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    // Create a test current user for substitution
    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // Base URL should have userId substituted
            Assert.Contains("123", execRequest.BaseUrl)
            Assert.DoesNotContain("@userId", execRequest.BaseUrl)

            // Headers should have token placeholder (not substituted since no token input provided)
            let authHeader =
                execRequest.Headers |> List.find (fun (k, _) -> k = "Authorization")

            Assert.Equal(("Authorization", "Bearer @token"), authHeader)

            // Body should have email substituted
            let emailBody = execRequest.Body |> List.find (fun (k, _) -> k = "email")
            Assert.Equal(("email", "john@example.com"), emailBody)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

// Input Type Validation Tests

[<Fact>]
let ``Run creation should validate Email input type with valid email`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "userEmail"
            Description = None
            Type = InputType.Email()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "email", "{userEmail}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "userEmail"
            Value = "test@example.com"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Email input type with invalid email`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "userEmail"
            Description = None
            Type = InputType.Email()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "email", "{userEmail}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "userEmail"
            Value = "invalid-email"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) -> Assert.Contains("Invalid email format", message)
    | _ -> Assert.True(false, "Expected validation error for invalid email")

[<Fact>]
let ``Run creation should validate Integer input type with valid integer`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "age"
            Description = None
            Type = InputType.Integer()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "age", "{age}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "age"; Value = "25" } ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Integer input type with invalid integer`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "age"
            Description = None
            Type = InputType.Integer()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "age", "{age}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "age"
            Value = "not-a-number"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("age", message)
        Assert.Contains("valid integer", message)
    | _ -> Assert.True(false, "Expected validation error for invalid integer")

[<Fact>]
let ``Run creation should validate Currency input type with 2 decimal places`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "amount"
            Description = None
            Type = InputType.Currency(SupportedCurrency.USD)
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/payments" [] [] [
            "amount", "{amount}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "amount"; Value = "123.45" } ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Currency input type with more than 2 decimal places`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "amount"
            Description = None
            Type = InputType.Currency(SupportedCurrency.USD)
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/payments" [] [] [
            "amount", "{amount}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "amount"; Value = "123.456" } ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("amount", message)
        Assert.Contains("2 decimal places", message)
    | _ -> Assert.True(false, "Expected validation error for invalid currency amount")

[<Fact>]
let ``Run creation should validate Boolean input type with valid boolean`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "isActive"
            Description = None
            Type = InputType.Boolean()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "active", "{isActive}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "isActive"; Value = "true" } ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Boolean input type with invalid boolean`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "isActive"
            Description = None
            Type = InputType.Boolean()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "active", "{isActive}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "isActive"; Value = "maybe" } ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("isActive", message)
        Assert.Contains("valid boolean", message)
    | _ -> Assert.True(false, "Expected validation error for invalid boolean")

[<Fact>]
let ``Run creation should validate Date input type with valid date`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "birthDate"
            Description = None
            Type = InputType.Date()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "birth_date", "{birthDate}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "birthDate"
            Value = "2023-01-15"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Date input type with invalid date`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "birthDate"
            Description = None
            Type = InputType.Date()
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "birth_date", "{birthDate}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "birthDate"
            Value = "not-a-date"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("birthDate", message)
        Assert.Contains("valid date", message)
    | _ -> Assert.True(false, "Expected validation error for invalid date")

[<Fact>]
let ``Run creation should validate Text input type within length limit`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "description"
            Description = None
            Type = InputType.Text(50) |> unwrapResult
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "desc", "{description}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "description"
            Value = "Short description"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject Text input type exceeding length limit`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "description"
            Description = None
            Type = InputType.Text(10) |> unwrapResult
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "desc", "{description}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "description"
            Value = "This description is way too long for the limit"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("description", message)
        Assert.Contains("maximum length", message)
        Assert.Contains("10", message)
    | _ -> Assert.True(false, "Expected validation error for text exceeding length limit")

[<Fact>]
let ``Run creation should validate MultiText input type with valid choice`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "priority"
            Description = None
            Type = InputType.MultiText(20, [ "high"; "medium"; "low" ]) |> unwrapResult
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "priority", "{priority}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [ { Title = "priority"; Value = "high" } ] // Valid text choice

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run creation should reject MultiText input type with invalid choice`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())
    let folderId = FolderId.NewId()

    let inputs = [
        {
            Title = "priority"
            Description = None
            Type = InputType.MultiText(20, [ "high"; "medium"; "low" ]) |> unwrapResult
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/users" [] [] [
            "priority", "{priority}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs (Some "/test") [] [] [] false None
        |> unwrapResult

    let inputValues = [
        {
            Title = "priority"
            Value = "invalid-choice"
        }
    ]

    // Act
    let result = Run.createWithValidation actorUserId app inputValues

    // Assert
    match result with
    | Error(ValidationError message) ->
        Assert.Contains("priority", message)
        Assert.Contains("allowed text values", message)
    | _ -> Assert.True(false, "Expected validation error for invalid MultiText choice")

// Variable Substitution Tests (Quoted Variables and Expressions)

/// Helper function to create test app with inputs that have spaces in names
let createTestAppWithQuotedVariables () =
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs = [
        {
            Title = "Customer ID"
            Description = None
            Type = InputType.Text(50) |> Result.defaultValue (InputType.Email())
            Required = true
            DefaultValue = None
        }
        {
            Title = "Amount"
            Description = None
            Type = InputType.Integer()
            Required = true
            DefaultValue = None
        }
        {
            Title = "Debit"
            Description = None
            Type = InputType.Boolean()
            Required = true
            DefaultValue = None
        }
    ]

    // Create a test resource with quoted variable syntax in URL params and body
    // (URL path can't have spaces, so we use URL params for variables with spaces)
    let resource =
        Resource.create
            actorUserId
            spaceId
            "Test API"
            "Test endpoint"
            "https://api.test.com/customers"
            [ "customer_id", "@\"Customer ID\""; "amount", "@\"Amount\"" ] [] []

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs None [] [] [] false None
        |> unwrapResult

    app, resource

[<Fact>]
let ``Run executable request should substitute quoted variable names with spaces`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithQuotedVariables ()

    let inputValues = [
        {
            Title = "Customer ID"
            Value = "CUST-12345"
        }
        { Title = "Amount"; Value = "10000" }
        { Title = "Debit"; Value = "true" }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // URL params should have "Customer ID" substituted
            let customerIdParam =
                execRequest.UrlParameters |> List.find (fun (k, _) -> k = "customer_id")

            Assert.Equal(("customer_id", "CUST-12345"), customerIdParam)
            Assert.DoesNotContain("@\"Customer ID\"", execRequest.UrlParameters |> List.map snd |> String.concat ",")

            // URL params should have Amount substituted
            let amountParam =
                execRequest.UrlParameters |> List.find (fun (k, _) -> k = "amount")

            Assert.Equal(("amount", "10000"), amountParam)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

/// Helper function to create test app with expression templates
let createTestAppWithExpressions () =
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs = [
        {
            Title = "Amount"
            Description = None
            Type = InputType.Integer()
            Required = true
            DefaultValue = None
        }
        {
            Title = "Debit"
            Description = None
            Type = InputType.Boolean()
            Required = true
            DefaultValue = None
        }
    ]

    // Create a test resource with expression template
    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/transactions" [] [] [
            "amount", "{{ @Debit ? -1 * @Amount : @Amount }}"
        ]

        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Post inputs None [] [] [] false None
        |> unwrapResult

    app, resource

/// Helper function to create test app with string comparison expression templates
let createTestAppWithStringComparisonExpression () =
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs = [
        {
            Title = "Cancel"
            Description = None
            Type = InputType.Text(100) |> unwrapResult
            Required = true
            DefaultValue = None
        }
    ]

    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com/transactions" [] [] [
            "shouldCancel", "{{ @Cancel == 'Immediately' ? true : false }}"
        ]
        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Post inputs None [] [] [] false None
        |> unwrapResult

    app, resource

[<Fact>]
let ``Run executable request should evaluate expression template with ternary and arithmetic (debit case)`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithExpressions ()

    let inputValues = [ { Title = "Amount"; Value = "10000" }; { Title = "Debit"; Value = "true" } ] // Debit is true, so amount should be negative

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // Body should have evaluated expression: -1 * 10000 = -10000
            let amountBody = execRequest.Body |> List.find (fun (k, _) -> k = "amount")
            Assert.Equal(("amount", "-10000"), amountBody)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run executable request should evaluate expression template with ternary and arithmetic (credit case)`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithExpressions ()

    let inputValues = [ { Title = "Amount"; Value = "5000" }; { Title = "Debit"; Value = "false" } ] // Debit is false, so amount should be positive

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // Body should have evaluated expression: amount stays positive
            let amountBody = execRequest.Body |> List.find (fun (k, _) -> k = "amount")
            Assert.Equal(("amount", "5000"), amountBody)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run executable request should evaluate string comparison ternary to true`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithStringComparisonExpression ()

    let inputValues = [
        {
            Title = "Cancel"
            Value = "Immediately"
        }
    ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            let cancelBody = execRequest.Body |> List.find (fun (k, _) -> k = "shouldCancel")
            Assert.Equal(("shouldCancel", "true"), cancelBody)
        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run executable request should evaluate string comparison ternary to false`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let app, resource = createTestAppWithStringComparisonExpression ()

    let inputValues = [ { Title = "Cancel"; Value = "Later" } ]

    let run = Run.createWithValidation actorUserId app inputValues |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "test-user-id"
        Email = "test@example.com"
        FirstName = "Test"
        LastName = "User"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            let cancelBody = execRequest.Body |> List.find (fun (k, _) -> k = "shouldCancel")
            Assert.Equal(("shouldCancel", "false"), cancelBody)
        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run executable request should substitute current_user variables`` () =
    // Arrange
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs: Input list = []

    let resource =
        Resource.create
            actorUserId
            spaceId
            "Test API"
            "Test endpoint"
            "https://api.test.com/users/@current_user.id"
            [ "email", "@current_user.email" ] [ "X-User-Name", "@current_user.firstName @current_user.lastName" ] []
        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs None [] [] [] false None
        |> unwrapResult

    let run = Run.createWithValidation actorUserId app [] |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "user-123"
        Email = "john@example.com"
        FirstName = "John"
        LastName = "Doe"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // Base URL should have current_user.id substituted
            Assert.Contains("user-123", execRequest.BaseUrl)
            Assert.DoesNotContain("@current_user.id", execRequest.BaseUrl)

            // URL params should have current_user.email substituted
            let emailParam = execRequest.UrlParameters |> List.find (fun (k, _) -> k = "email")

            Assert.Equal(("email", "john@example.com"), emailParam)

            // Headers should have current_user names substituted
            let userHeader = execRequest.Headers |> List.find (fun (k, _) -> k = "X-User-Name")

            Assert.Equal(("X-User-Name", "John Doe"), userHeader)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``Run executable request should substitute quoted current_user variables`` () =
    // Arrange - test the quoted form @"current_user.email" which is used when names contain dots
    let folderId = FolderId.NewId()
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let spaceId = SpaceId.FromGuid(Guid.NewGuid())

    let inputs: Input list = []

    // Use quoted syntax for current_user variables (as the frontend serializes them)
    let resource =
        Resource.create actorUserId spaceId "Test API" "Test endpoint" "https://api.test.com" [] [
            "initiator-email", "@\"current_user.email\""
        ] []
        |> unwrapResult

    let app =
        App.create actorUserId "Test App" folderId resource HttpMethod.Get inputs None [] [] [] false None
        |> unwrapResult

    let run = Run.createWithValidation actorUserId app [] |> unwrapResult

    let testCurrentUser: CurrentUser = {
        Id = "user-123"
        Email = "john@example.com"
        FirstName = "John"
        LastName = "Doe"
    }

    // Act
    let result =
        Run.composeExecutableRequestFromAppAndResource run app resource testCurrentUser None

    // Assert
    match result with
    | Ok runWithRequest ->
        match Run.getExecutableRequest runWithRequest with
        | Some execRequest ->
            // Headers should have quoted current_user.email substituted
            let emailHeader =
                execRequest.Headers |> List.find (fun (k, _) -> k = "initiator-email")

            Assert.Equal(("initiator-email", "john@example.com"), emailHeader)

        | None -> Assert.True(false, "Expected executable request to be set")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")