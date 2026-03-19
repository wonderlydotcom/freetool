module Freetool.Infrastructure.Tests.AutoTracingTests

open System
open System.Diagnostics
open System.Threading.Tasks
open Xunit
open Freetool.Api.Tracing
open Freetool.Application.Interfaces
open Freetool.Domain

// Test command types for AutoTracing tests
type TestCommandResult =
    | TestResult of string
    | TestsResult of string list

type TestCommand =
    | CreateTestItem of name: string
    | GetTestItem of id: string
    | FindTestItems of query: string
    | ListTestItems of skip: int * take: int
    | UpdateTestItem of id: string * name: string
    | SetTestItemName of id: string * name: string
    | MoveTestItem of id: string * targetId: string
    | NameUpdateTest of id: string // Contains "update" but doesn't start with it
    | DeleteTestItem of id: string
    | RemoveTestItem of id: string
    | SomeOtherAction of data: string

// Test DTO records for attribute extraction
type TestDto = { UserName: string; Email: string }

type SensitiveDto = {
    Password: string
    ApiToken: string
    SecretKey: string
}

// ============================================================================
// Span Naming Tests
// ============================================================================

[<Fact>]
let ``getSpanName converts PascalCase to snake_case`` () =
    // Arrange
    let command = CreateTestItem "test"
    let entityName = "app"

    // Act
    let spanName = AutoTracing.getSpanName entityName command

    // Assert
    Assert.Equal("app.create_test_item", spanName)

[<Fact>]
let ``getSpanName includes entity prefix`` () =
    // Arrange
    let command = GetTestItem "123"
    let entityName = "user"

    // Act
    let spanName = AutoTracing.getSpanName entityName command

    // Assert
    Assert.StartsWith("user.", spanName)
    Assert.Equal("user.get_test_item", spanName)

// ============================================================================
// Operation Type Classification Tests
// ============================================================================

[<Fact>]
let ``getOperationType identifies create operations`` () =
    // Arrange
    let command = CreateTestItem "test"

    // Act
    let operationType = AutoTracing.getOperationType command

    // Assert
    Assert.Equal("create", operationType)

[<Theory>]
[<InlineData("get")>]
[<InlineData("find")>]
[<InlineData("list")>]
let ``getOperationType identifies read operations`` (expectedPrefix: string) =
    // Arrange
    let command =
        match expectedPrefix with
        | "get" -> GetTestItem "123" :> obj
        | "find" -> FindTestItems "query" :> obj
        | "list" -> ListTestItems(0, 10) :> obj
        | _ -> failwith "Unexpected prefix"

    // Act
    let operationType = AutoTracing.getOperationType command

    // Assert
    Assert.Equal("read", operationType)

[<Theory>]
[<InlineData("update")>]
[<InlineData("set")>]
[<InlineData("move")>]
[<InlineData("contains_update")>]
let ``getOperationType identifies update operations`` (operationVariant: string) =
    // Arrange
    let command =
        match operationVariant with
        | "update" -> UpdateTestItem("123", "name") :> obj
        | "set" -> SetTestItemName("123", "name") :> obj
        | "move" -> MoveTestItem("123", "456") :> obj
        | "contains_update" -> NameUpdateTest "123" :> obj
        | _ -> failwith "Unexpected variant"

    // Act
    let operationType = AutoTracing.getOperationType command

    // Assert
    Assert.Equal("update", operationType)

[<Theory>]
[<InlineData("delete")>]
[<InlineData("remove")>]
let ``getOperationType identifies delete operations`` (operationVariant: string) =
    // Arrange
    let command =
        match operationVariant with
        | "delete" -> DeleteTestItem "123" :> obj
        | "remove" -> RemoveTestItem "123" :> obj
        | _ -> failwith "Unexpected variant"

    // Act
    let operationType = AutoTracing.getOperationType command

    // Assert
    Assert.Equal("delete", operationType)

// ============================================================================
// Security Filtering Tests
// ============================================================================

[<Theory>]
[<InlineData("password")>]
[<InlineData("Password")>]
[<InlineData("userPassword")>]
[<InlineData("PASSWORD")>]
let ``shouldSkipField skips password fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.True(shouldSkip, $"Field '{fieldName}' should be skipped")

[<Theory>]
[<InlineData("token")>]
[<InlineData("Token")>]
[<InlineData("apiToken")>]
[<InlineData("accessToken")>]
[<InlineData("TOKEN")>]
let ``shouldSkipField skips token fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.True(shouldSkip, $"Field '{fieldName}' should be skipped")

[<Theory>]
[<InlineData("userName")>]
[<InlineData("email")>]
[<InlineData("id")>]
[<InlineData("name")>]
[<InlineData("description")>]
[<InlineData("createdAt")>]
let ``shouldSkipField allows normal fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.False(shouldSkip, $"Field '{fieldName}' should not be skipped")

// ============================================================================
// Additional Security Filtering Tests
// ============================================================================

[<Theory>]
[<InlineData("secret")>]
[<InlineData("clientSecret")>]
[<InlineData("SECRET_KEY")>]
let ``shouldSkipField skips secret fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.True(shouldSkip, $"Field '{fieldName}' should be skipped")

[<Theory>]
[<InlineData("key")>]
[<InlineData("apiKey")>]
[<InlineData("privateKey")>]
let ``shouldSkipField skips key fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.True(shouldSkip, $"Field '{fieldName}' should be skipped")

[<Theory>]
[<InlineData("credential")>]
[<InlineData("userCredential")>]
[<InlineData("CREDENTIALS")>]
let ``shouldSkipField skips credential fields`` (fieldName: string) =
    // Act
    let shouldSkip = AutoTracing.shouldSkipField fieldName

    // Assert
    Assert.True(shouldSkip, $"Field '{fieldName}' should be skipped")

// ============================================================================
// Tracing Decorator Tests
// ============================================================================

// Mock handler for testing the tracing decorator
type MockHandler(returnError: bool) =
    let mutable commandsHandled = ResizeArray<TestCommand>()

    member this.CommandsHandled = commandsHandled :> seq<TestCommand>

    interface ICommandHandler<TestCommand, TestCommandResult> with
        member this.HandleCommand command = task {
            commandsHandled.Add(command)

            if returnError then
                return Error(DomainError.ValidationError "Test error")
            else
                return Ok(TestResult "success")
        }

[<Fact>]
let ``createTracingDecorator creates span on command execution`` () : Task = task {
    // Arrange
    let activitySource = new ActivitySource("test.autotracing")

    let listener =
        new ActivityListener(
            ShouldListenTo = (fun source -> source.Name = "test.autotracing"),
            Sample = (fun _ -> ActivitySamplingResult.AllData),
            ActivityStarted = (fun _ -> ()),
            ActivityStopped = (fun _ -> ())
        )

    ActivitySource.AddActivityListener(listener)

    let mockHandler = MockHandler(false)

    let tracingHandler =
        AutoTracing.createTracingDecorator "test" mockHandler activitySource

    // Act
    let! result = tracingHandler.HandleCommand(CreateTestItem "myItem")

    // Assert
    Assert.True(Result.isOk result)
    Assert.Single(mockHandler.CommandsHandled) |> ignore

    // Cleanup
    listener.Dispose()
    activitySource.Dispose()
}

[<Fact>]
let ``createTracingDecorator adds command attributes`` () : Task = task {
    // Arrange
    let activitySource = new ActivitySource("test.autotracing.attributes")
    let mutable capturedActivity: Activity option = None

    let listener =
        new ActivityListener(
            ShouldListenTo = (fun source -> source.Name = "test.autotracing.attributes"),
            Sample = (fun _ -> ActivitySamplingResult.AllDataAndRecorded),
            ActivityStarted = (fun activity -> capturedActivity <- Some activity),
            ActivityStopped = (fun _ -> ())
        )

    ActivitySource.AddActivityListener(listener)

    let mockHandler = MockHandler(false)

    let tracingHandler =
        AutoTracing.createTracingDecorator "entity" mockHandler activitySource

    // Act
    let! _ = tracingHandler.HandleCommand(CreateTestItem "testName")

    // Assert - verify the span was created with correct name
    match capturedActivity with
    | Some activity ->
        Assert.Equal("entity.create_test_item", activity.OperationName)
        // The operation.type attribute should be set
        let operationTypeTag = activity.GetTagItem("operation.type")
        Assert.NotNull(operationTypeTag)
        Assert.Equal("create", operationTypeTag :?> string)
    | None -> Assert.True(false, "No activity was captured")

    // Cleanup
    listener.Dispose()
    activitySource.Dispose()
}

[<Fact>]
let ``createTracingDecorator adds error event on failure`` () : Task = task {
    // Arrange
    let activitySource = new ActivitySource("test.autotracing.errors")
    let mutable stoppedActivity: Activity option = None

    let listener =
        new ActivityListener(
            ShouldListenTo = (fun source -> source.Name = "test.autotracing.errors"),
            Sample = (fun _ -> ActivitySamplingResult.AllDataAndRecorded),
            ActivityStarted = (fun _ -> ()),
            ActivityStopped = (fun activity -> stoppedActivity <- Some activity)
        )

    ActivitySource.AddActivityListener(listener)

    let mockHandler = MockHandler(true) // Will return error

    let tracingHandler =
        AutoTracing.createTracingDecorator "entity" mockHandler activitySource

    // Act
    let! result = tracingHandler.HandleCommand(CreateTestItem "testName")

    // Assert
    Assert.True(Result.isError result)

    match stoppedActivity with
    | Some activity ->
        // Verify error status is set
        Assert.Equal(ActivityStatusCode.Error, activity.Status)

        // Verify error tags are present
        let errorType = activity.GetTagItem("error.type")
        Assert.NotNull(errorType)
        Assert.Equal("ValidationError", errorType :?> string)

        let errorMessage = activity.GetTagItem("error.message")
        Assert.NotNull(errorMessage)
        Assert.Equal("Test error", errorMessage :?> string)

        // Verify domain_error event was added
        let events = activity.Events |> Seq.toList
        let domainErrorEvent = events |> List.tryFind (fun e -> e.Name = "domain_error")
        Assert.True(domainErrorEvent.IsSome, "domain_error event should be present")
    | None -> Assert.True(false, "No activity was captured")

    // Cleanup
    listener.Dispose()
    activitySource.Dispose()
}

// ============================================================================
// Additional Attribute Name Conversion Tests
// ============================================================================

[<Fact>]
let ``getAttributeName converts PascalCase to snake_case with prefix`` () =
    // Arrange
    let prefix = "user"
    let fieldName = "UserName"

    // Act
    let attributeName = AutoTracing.getAttributeName prefix fieldName

    // Assert
    Assert.Equal("user.user_name", attributeName)

[<Fact>]
let ``getOperationType returns unknown for non-standard operations`` () =
    // Arrange
    let command = SomeOtherAction "data"

    // Act
    let operationType = AutoTracing.getOperationType command

    // Assert
    Assert.Equal("unknown", operationType)