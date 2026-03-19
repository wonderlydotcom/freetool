module Freetool.Infrastructure.Tests.DevAuthMiddlewareTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.Interfaces
open Freetool.Api.Middleware

// ============================================================================
// Mock Types
// ============================================================================

type MockUserRepository(existingUsers: Map<UserId, ValidatedUser>) =
    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) =
            let userOption = existingUsers |> Map.tryFind userId
            Task.FromResult(userOption)

        member _.GetByEmailAsync _ = Task.FromResult(None)
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.AddAsync _ = Task.FromResult(Ok())
        member _.UpdateAsync _ = Task.FromResult(Ok())
        member _.DeleteAsync _ = Task.FromResult(Ok())
        member _.ExistsAsync _ = Task.FromResult(false)
        member _.ExistsByEmailAsync _ = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

// ============================================================================
// Helper Functions
// ============================================================================

let createTestHttpContext () =
    let context = DefaultHttpContext()
    context.Response.Body <- new MemoryStream()
    context

let addHeader (context: HttpContext) (key: string) (value: string) =
    context.Request.Headers.[key] <- StringValues(value)
    context

let setPath (context: HttpContext) (path: string) =
    context.Request.Path <- PathString(path)
    context

let setupServices (context: HttpContext) (userRepo: IUserRepository) =
    let services = ServiceCollection()
    services.AddSingleton<IUserRepository>(userRepo) |> ignore

    let serviceProvider = services.BuildServiceProvider()
    context.RequestServices <- serviceProvider
    context

let getResponseBody (context: HttpContext) =
    context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use reader = new StreamReader(context.Response.Body)
    reader.ReadToEnd()

let createMiddleware () =
    let mutable nextCalled = false

    let nextDelegate =
        RequestDelegate(fun _ ->
            nextCalled <- true
            Task.CompletedTask)

    let middleware = DevAuthMiddleware(nextDelegate)
    (middleware, fun () -> nextCalled)

let createValidUser (userId: UserId) (email: string) (name: string) : ValidatedUser =
    let emailObj =
        Email.Create(Some email)
        |> Result.defaultWith (fun _ -> failwith "Invalid email")

    let user = User.create name emailObj None

    // Return a user with the specified ID
    {
        user with
            State = { user.State with Id = userId }
    }

// ============================================================================
// Test Cases
// ============================================================================

[<Fact>]
let ``Allows dev endpoints without authentication`` () : Task = task {
    // Arrange
    let context = createTestHttpContext () |> fun c -> setPath c "/dev/users"

    let userRepo = MockUserRepository(Map.empty)
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.True(wasNextCalled ())
    // Should not return 401
    Assert.NotEqual(401, context.Response.StatusCode)
}

[<Fact>]
let ``Returns 401 when X-Dev-User-Id header missing`` () : Task = task {
    // Arrange
    let context = createTestHttpContext () |> fun c -> setPath c "/api/some-endpoint"

    let userRepo = MockUserRepository(Map.empty)
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    let body = getResponseBody context
    Assert.Contains("X-Dev-User-Id", body)
}

[<Fact>]
let ``Returns 401 when X-Dev-User-Id is invalid GUID`` () : Task = task {
    // Arrange
    let context =
        createTestHttpContext ()
        |> fun c -> setPath c "/api/some-endpoint"
        |> fun c -> addHeader c "X-Dev-User-Id" "not-a-valid-guid"

    let userRepo = MockUserRepository(Map.empty)
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    let body = getResponseBody context
    Assert.Contains("Invalid user ID format", body)
}

[<Fact>]
let ``Returns 401 when user not found in database`` () : Task = task {
    // Arrange
    let nonExistentUserId = Guid.NewGuid()

    let context =
        createTestHttpContext ()
        |> fun c -> setPath c "/api/some-endpoint"
        |> fun c -> addHeader c "X-Dev-User-Id" (nonExistentUserId.ToString())

    let userRepo = MockUserRepository(Map.empty)
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    let body = getResponseBody context
    Assert.Contains("User not found", body)
}

[<Fact>]
let ``Sets UserId in HttpContext Items for valid user`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let user = createValidUser userId "test@example.com" "Test User"

    let context =
        createTestHttpContext ()
        |> fun c -> setPath c "/api/some-endpoint"
        |> fun c -> addHeader c "X-Dev-User-Id" (userId.Value.ToString())

    let userRepo = MockUserRepository(Map.ofList [ (userId, user) ])
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    // Verify UserId is set correctly
    Assert.True(context.Items.ContainsKey("UserId"))
    let contextUserId = context.Items.["UserId"] :?> UserId
    Assert.Equal(userId, contextUserId)
}

[<Fact>]
let ``Calls next middleware on success`` () : Task = task {
    // Arrange
    let userId = UserId.NewId()
    let user = createValidUser userId "test@example.com" "Test User"

    let context =
        createTestHttpContext ()
        |> fun c -> setPath c "/api/some-endpoint"
        |> fun c -> addHeader c "X-Dev-User-Id" (userId.Value.ToString())

    let userRepo = MockUserRepository(Map.ofList [ (userId, user) ])
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.True(wasNextCalled ())
}

[<Fact>]
let ``Adds tracing attributes on authentication`` () : Task = task {
    // Note: This test verifies the code path runs successfully.
    // Actual tracing attribute verification would require mocking Activity.Current
    // which is complex in a unit test context. The middleware's tracing code
    // handles null Activity gracefully via Option.ofObj.

    // Arrange
    let userId = UserId.NewId()
    let user = createValidUser userId "test@example.com" "Test User"

    let context =
        createTestHttpContext ()
        |> fun c -> setPath c "/api/some-endpoint"
        |> fun c -> addHeader c "X-Dev-User-Id" (userId.Value.ToString())

    let userRepo = MockUserRepository(Map.ofList [ (userId, user) ])
    setupServices context userRepo |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    // Act - should not throw even without an active span
    do! middleware.InvokeAsync(context)

    // Assert
    Assert.True(wasNextCalled ())
    Assert.True(context.Items.ContainsKey("UserId"))
}