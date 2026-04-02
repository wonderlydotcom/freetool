module Freetool.Infrastructure.Tests.IapAuthMiddlewareTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Microsoft.IdentityModel.Tokens
open System.IdentityModel.Tokens.Jwt
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Api.Auth
open Freetool.Api.Middleware
open Freetool.Api.Services

// ============================================================================
// Mock Types
// ============================================================================

type MockUserRepository
    (
        existingUsers: Map<string, ValidatedUser>,
        addResult: Result<unit, DomainError>,
        updateResult: Result<unit, DomainError>
    ) =
    let mutable users = existingUsers
    let mutable addedUsers: ValidatedUser list = []
    let mutable updatedUsers: ValidatedUser list = []

    member _.GetAddedUsers() = addedUsers
    member _.GetUpdatedUsers() = updatedUsers

    interface IUserRepository with
        member _.GetByIdAsync(userId: UserId) =
            let userOption =
                users
                |> Map.tryPick (fun _ user -> if user.State.Id = userId then Some user else None)

            Task.FromResult(userOption)

        member _.GetByEmailAsync(email: Email) =
            let userOption = users |> Map.tryFind email.Value

            Task.FromResult(userOption)

        member _.GetAllAsync _ _ = Task.FromResult([])

        member _.AddAsync(user: ValidatedUser) =
            addedUsers <- addedUsers @ [ user ]
            users <- users.Add(user.State.Email, user)
            Task.FromResult(addResult)

        member _.UpdateAsync(user: ValidatedUser) =
            updatedUsers <- updatedUsers @ [ user ]
            users <- users.Add(user.State.Email, user)
            Task.FromResult(updateResult)

        member _.DeleteAsync _ = Task.FromResult(Ok())
        member _.ExistsAsync _ = Task.FromResult(false)
        member _.ExistsByEmailAsync _ = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

type MockAuthorizationService(initOrgResult: Result<unit, exn>) =
    let mutable initOrgCalls: (string * string) list = []

    member _.GetInitOrgCalls() = initOrgCalls

    interface IAuthorizationService with
        member _.CreateStoreAsync _ =
            Task.FromResult({ Id = "store-1"; Name = "test-store" })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync orgId userId = task {
            initOrgCalls <- initOrgCalls @ [ (orgId, userId) ]

            match initOrgResult with
            | Ok() -> return ()
            | Error ex -> return raise ex
        }

        member _.CreateRelationshipsAsync _ = Task.FromResult(())
        member _.UpdateRelationshipsAsync _ = Task.FromResult(())
        member _.DeleteRelationshipsAsync _ = Task.FromResult(())

        member _.CheckPermissionAsync _ _ _ = Task.FromResult(false)
        member _.StoreExistsAsync _ = Task.FromResult(true)
        member _.BatchCheckPermissionsAsync _ _ _ = Task.FromResult(Map.empty)

type MockIdentityGroupSpaceMappingRepository() =
    interface IIdentityGroupSpaceMappingRepository with
        member _.GetAllAsync() = Task.FromResult([])
        member _.GetSpaceIdsByGroupKeysAsync _ = Task.FromResult([])

        member _.AddAsync _ _ _ =
            Task.FromResult(Error(InvalidOperation "Not used in middleware tests"))

        member _.UpdateIsActiveAsync _ _ _ =
            Task.FromResult(Error(InvalidOperation "Not used in middleware tests"))

        member _.DeleteAsync _ =
            Task.FromResult(Error(InvalidOperation "Not used in middleware tests"))

type MockGoogleDirectoryIdentityService(?identityData: GoogleDirectoryIdentityData) =
    let identityData = defaultArg identityData { GroupKeys = []; ProfilePicUrl = None }

    interface IGoogleDirectoryIdentityService with
        member _.GetIdentityGroupKeysAsync _ = Task.FromResult(identityData.GroupKeys)
        member _.GetIdentityDataAsync _ = Task.FromResult(identityData)

type MockSpaceRepository() =
    interface ISpaceRepository with
        member _.GetByIdAsync _ = Task.FromResult(None)
        member _.GetByNameAsync _ = Task.FromResult(None)
        member _.GetAllAsync _ _ = Task.FromResult([])
        member _.GetByUserIdAsync _ = Task.FromResult([])
        member _.GetByModeratorUserIdAsync _ = Task.FromResult([])
        member _.AddAsync _ = Task.FromResult(Ok())
        member _.UpdateAsync _ = Task.FromResult(Ok())
        member _.DeleteAsync _ = Task.FromResult(Ok())
        member _.ExistsAsync _ = Task.FromResult(false)
        member _.ExistsByNameAsync _ = Task.FromResult(false)
        member _.GetCountAsync() = Task.FromResult(0)

type StubHttpMessageHandler(responder: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        responder request |> Task.FromResult

type StubHttpClientFactory(client: HttpClient) =
    interface IHttpClientFactory with
        member _.CreateClient(_name: string) = client

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

let createJwtTestMaterial (audience: string) (email: string) =
    let rsa = RSA.Create(2048)
    let key = RsaSecurityKey(rsa.ExportParameters(true))
    key.KeyId <- "test-key"

    let signingCredentials = SigningCredentials(key, SecurityAlgorithms.RsaSha256)
    let now = DateTime.UtcNow

    let token =
        JwtSecurityToken(
            issuer = "https://cloud.google.com/iap",
            audience = audience,
            claims = [ Claim("email", email); Claim("sub", $"sub-{email}") ],
            notBefore = Nullable(now.AddMinutes(-1.0)),
            expires = Nullable(now.AddMinutes(5.0)),
            signingCredentials = signingCredentials
        )
        |> JwtSecurityTokenHandler().WriteToken

    let jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key)
    jwk.Kid <- key.KeyId
    let jwksJson = JsonSerializer.Serialize({| keys = [| jwk |] |})

    rsa.Dispose()
    token, jwksJson

let setupServicesWithDirectoryCore
    (context: HttpContext)
    (userRepo: IUserRepository)
    (authService: IAuthorizationService)
    (orgAdminEmail: string option)
    (additionalConfig: (string * string) list)
    (directoryIdentityData: GoogleDirectoryIdentityData option)
    (jwksJson: string option)
    =
    let services = ServiceCollection()
    services.AddSingleton<IUserRepository>(userRepo) |> ignore
    services.AddSingleton<IAuthorizationService>(authService) |> ignore

    let configValues =
        [
            "Auth:IAP:ValidateJwt", "false"
            match orgAdminEmail with
            | Some email -> "OpenFGA:OrgAdminEmail", email
            | None -> ()
            yield! additionalConfig
        ]
        |> dict

    let config = ConfigurationBuilder().AddInMemoryCollection(configValues).Build()

    services.AddSingleton<IConfiguration>(config) |> ignore

    match jwksJson with
    | Some currentJwksJson ->
        let messageHandler =
            new StubHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                match request.RequestUri with
                | null ->
                    response.StatusCode <- HttpStatusCode.BadRequest
                    response.Content <- new StringContent("")
                    response
                | _ ->
                    response.Content <- new StringContent(currentJwksJson)
                    response)

        services.AddSingleton<IHttpClientFactory>(StubHttpClientFactory(new HttpClient(messageHandler)))
        |> ignore
    | None -> ()

    services.AddSingleton<IIdentityGroupSpaceMappingRepository>(MockIdentityGroupSpaceMappingRepository())
    |> ignore

    services.AddSingleton<IGoogleDirectoryIdentityService>(
        MockGoogleDirectoryIdentityService(?identityData = directoryIdentityData)
    )
    |> ignore

    services.AddSingleton<ISpaceRepository>(MockSpaceRepository()) |> ignore

    services.AddSingleton<IIdentityProvisioningService>(fun serviceProvider ->
        let userRepository = serviceProvider.GetRequiredService<IUserRepository>()

        let authorizationService =
            serviceProvider.GetRequiredService<IAuthorizationService>()

        let mappingRepository =
            serviceProvider.GetRequiredService<IIdentityGroupSpaceMappingRepository>()

        let spaceRepository = serviceProvider.GetRequiredService<ISpaceRepository>()

        let configuration = serviceProvider.GetRequiredService<IConfiguration>()

        IdentityProvisioningService(
            userRepository,
            authorizationService,
            mappingRepository,
            spaceRepository,
            configuration,
            NullLogger<IdentityProvisioningService>.Instance
        )
        :> IIdentityProvisioningService)
    |> ignore

    let serviceProvider = services.BuildServiceProvider()
    context.RequestServices <- serviceProvider
    context

let setupServicesWithDirectory
    (context: HttpContext)
    (userRepo: IUserRepository)
    (authService: IAuthorizationService)
    (orgAdminEmail: string option)
    (additionalConfig: (string * string) list)
    (directoryIdentityData: GoogleDirectoryIdentityData option)
    =
    setupServicesWithDirectoryCore
        context
        userRepo
        authService
        orgAdminEmail
        additionalConfig
        directoryIdentityData
        None

let setupServicesWithJwtDirectory
    (context: HttpContext)
    (userRepo: IUserRepository)
    (authService: IAuthorizationService)
    (orgAdminEmail: string option)
    (additionalConfig: (string * string) list)
    (directoryIdentityData: GoogleDirectoryIdentityData option)
    (jwksJson: string)
    =
    setupServicesWithDirectoryCore
        context
        userRepo
        authService
        orgAdminEmail
        additionalConfig
        directoryIdentityData
        (Some jwksJson)

let setupServices
    (context: HttpContext)
    (userRepo: IUserRepository)
    (authService: IAuthorizationService)
    (orgAdminEmail: string option)
    (additionalConfig: (string * string) list)
    =
    setupServicesWithDirectory context userRepo authService orgAdminEmail additionalConfig None

let getResponseBody (context: HttpContext) =
    context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
    use reader = new StreamReader(context.Response.Body)
    reader.ReadToEnd()

let getResponseCode (context: HttpContext) =
    use document = getResponseBody context |> JsonDocument.Parse
    document.RootElement.GetProperty("code").GetString()

let createMiddleware () =
    let mutable nextCalled = false

    let nextDelegate =
        RequestDelegate(fun _ ->
            nextCalled <- true
            Task.CompletedTask)

    let middleware =
        IapAuthMiddleware(nextDelegate, NullLogger<IapAuthMiddleware>.Instance)

    (middleware, fun () -> nextCalled)

let createValidUser (email: string) (name: string) : ValidatedUser =
    let emailObj =
        Email.Create(Some email)
        |> Result.defaultWith (fun _ -> failwith "Invalid email")

    User.create name emailObj None

let createInvitedPlaceholderUser (email: string) : ValidatedUser =
    let emailObj =
        Email.Create(Some email)
        |> Result.defaultWith (fun _ -> failwith "Invalid email")

    User.invite (UserId.NewId()) emailObj

// ============================================================================
// Test Cases
// ============================================================================

[<Fact>]
let ``Allows health checks without IAP headers`` () : Task = task {
    let context = createTestHttpContext ()
    context.Request.Path <- PathString("/healthy")
    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.True(wasNextCalled ())
    Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode)
}

[<Fact>]
let ``Does not bypass non-canonical health paths`` () : Task = task {
    let context = createTestHttpContext ()
    context.Request.Path <- PathString("/healthz")
    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())
}

[<Fact>]
let ``Returns 401 when IAP email header missing`` () : Task = task {
    let context = createTestHttpContext ()
    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("application/problem+json", context.Response.ContentType)
    Assert.Equal("missing_iap_email_header", getResponseCode context)
}

[<Fact>]
let ``Returns 401 when JWT validation enabled and JWT assertion header missing`` () : Task = task {
    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" "user@example.com"

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServices context userRepo authService None [
        "Auth:IAP:ValidateJwt", "true"
        "IAP_JWT_AUDIENCE", "/projects/123/global/backendServices/456"
    ]
    |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("missing_iap_jwt_assertion", getResponseCode context)
}

[<Fact>]
let ``Uses JWT email when JWT validation enabled and IAP email header missing`` () : Task = task {
    let email = "jwt-user@example.com"
    let audience = "/projects/123/global/backendServices/456"
    let jwtAssertion, jwksJson = createJwtTestMaterial audience email

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Iap-Jwt-Assertion" jwtAssertion

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServicesWithJwtDirectory
        context
        userRepo
        authService
        None
        [
            "Auth:IAP:ValidateJwt", "true"
            "IAP_JWT_AUDIENCE", audience
            "Auth:IAP:JwtCertsUrl", "https://example.test/jwks"
        ]
        None
        jwksJson
    |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(email, addedUsers.[0].State.Email)

    match RequestUserContext.tryGet context with
    | None -> failwith "Expected request user context"
    | Some requestUser -> Assert.Equal(email, requestUser.Email)
}

[<Fact>]
let ``Uses JWT email when JWT validation enabled and IAP email header differs`` () : Task = task {
    let tokenEmail = "jwt-user@example.com"
    let audience = "/projects/123/global/backendServices/456"
    let jwtAssertion, jwksJson = createJwtTestMaterial audience tokenEmail

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Iap-Jwt-Assertion" jwtAssertion
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" "accounts.google.com:header-user@example.com"

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServicesWithJwtDirectory
        context
        userRepo
        authService
        None
        [
            "Auth:IAP:ValidateJwt", "true"
            "IAP_JWT_AUDIENCE", audience
            "Auth:IAP:JwtCertsUrl", "https://example.test/jwks"
        ]
        None
        jwksJson
    |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(tokenEmail, addedUsers.[0].State.Email)

    match RequestUserContext.tryGet context with
    | None -> failwith "Expected request user context"
    | Some requestUser -> Assert.Equal(tokenEmail, requestUser.Email)
}

[<Fact>]
let ``Returns 401 when IAP email header empty`` () : Task = task {
    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" ""

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("missing_iap_email_header", getResponseCode context)
}

[<Fact>]
let ``Returns 401 when email format invalid`` () : Task = task {
    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" "accounts.google.com:not-a-valid-email"

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(401, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("invalid_iap_email", getResponseCode context)
}

[<Fact>]
let ``Creates new user with IAP display name when email not in database`` () : Task = task {
    let email = "newuser@example.com"
    let displayName = "New User"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" $"accounts.google.com:{email}"
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Name" displayName

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(email, addedUsers.[0].State.Email)
    Assert.Equal(displayName, addedUsers.[0].State.Name)

    Assert.True(context.Items.ContainsKey("UserId"))

    match RequestUserContext.tryGet context with
    | None -> failwith "Expected request user context"
    | Some requestUser ->
        Assert.Equal(email, requestUser.Email)
        Assert.Equal(Some displayName, requestUser.Name)
        Assert.Empty(requestUser.GroupKeys)
        Assert.Equal("iap", requestUser.AuthenticationSource)
}

[<Fact>]
let ``Sets UserId in HttpContext Items for existing user`` () : Task = task {
    let email = "existing@example.com"
    let existingUser = createValidUser email "Existing User"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email

    let userRepo = MockUserRepository(Map.ofList [ (email, existingUser) ], Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())
    Assert.Empty(userRepo.GetAddedUsers())

    Assert.True(context.Items.ContainsKey("UserId"))
    let userId = context.Items.["UserId"] :?> UserId
    Assert.Equal(existingUser.State.Id, userId)

    match RequestUserContext.tryGet context with
    | None -> failwith "Expected request user context"
    | Some requestUser ->
        Assert.Equal(email, requestUser.Email)
        Assert.Equal(Some existingUser.State.Id, requestUser.UserId)
}

[<Fact>]
let ``Activates invited placeholder user on first login`` () : Task = task {
    let email = "invited@example.com"
    let invitedUser = createInvitedPlaceholderUser email
    let userName = "Invited User Name"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Name" userName

    let userRepo = MockUserRepository(Map.ofList [ (email, invitedUser) ], Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    Assert.Empty(userRepo.GetAddedUsers())
    let updatedUsers = userRepo.GetUpdatedUsers()
    Assert.Single(updatedUsers) |> ignore

    let activatedUser = updatedUsers.[0]
    Assert.Equal(userName, activatedUser.State.Name)
    Assert.True(activatedUser.State.InvitedAt.IsNone)

    Assert.True(context.Items.ContainsKey("UserId"))
}

[<Fact>]
let ``Initializes org admin when email matches config`` () : Task = task {
    let email = "admin@example.com"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService (Some email) [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    let initOrgCalls = authService.GetInitOrgCalls()
    Assert.Single(initOrgCalls) |> ignore
    Assert.Equal("default", fst initOrgCalls.[0])
}

[<Fact>]
let ``Calls next middleware on success`` () : Task = task {
    let email = "test@example.com"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.True(wasNextCalled ())
}

[<Fact>]
let ``Returns 500 when user creation fails`` () : Task = task {
    let email = "newuser@example.com"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email

    let userRepo =
        MockUserRepository(Map.empty, Error(ValidationError "Database error"), Ok())

    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(500, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("iap_identity_provisioning_failed", getResponseCode context)
}

[<Fact>]
let ``Returns 500 when user activation fails`` () : Task = task {
    let email = "invited@example.com"
    let invitedUser = createInvitedPlaceholderUser email

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Name" "User Name"

    let userRepo =
        MockUserRepository(Map.ofList [ (email, invitedUser) ], Ok(), Error(ValidationError "Update failed"))

    let authService = MockAuthorizationService(Ok())
    setupServices context userRepo authService None [] |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(500, context.Response.StatusCode)
    Assert.False(wasNextCalled ())

    Assert.Equal("iap_identity_provisioning_failed", getResponseCode context)
}

[<Fact>]
let ``Uses configured name header for display name`` () : Task = task {
    let email = "newuser@example.com"
    let displayName = "John Doe"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email
        |> fun c -> addHeader c "X-Display-Name" displayName

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServices context userRepo authService None [ "Auth:IAP:NameHeader", "X-Display-Name" ]
    |> ignore

    let middleware, _ = createMiddleware ()

    do! middleware.InvokeAsync(context)

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(displayName, addedUsers.[0].State.Name)
}

[<Fact>]
let ``Uses configured picture header for avatar`` () : Task = task {
    let email = "newuser@example.com"
    let profilePicUrl = "https://example.com/avatar.jpg"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email
        |> fun c -> addHeader c "X-Avatar" profilePicUrl

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServices context userRepo authService None [ "Auth:IAP:PictureHeader", "X-Avatar" ]
    |> ignore

    let middleware, _ = createMiddleware ()

    do! middleware.InvokeAsync(context)

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(Some profilePicUrl, addedUsers.[0].State.ProfilePicUrl)
}

[<Fact>]
let ``Uses directory profile picture for avatar when available`` () : Task = task {
    let email = "newuser@example.com"
    let directoryProfilePicUrl = "https://lh3.googleusercontent.com/a-/directory-avatar"

    let context =
        createTestHttpContext ()
        |> fun c -> addHeader c "X-Goog-Authenticated-User-Email" email

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    let directoryIdentityData: GoogleDirectoryIdentityData = {
        GroupKeys = []
        ProfilePicUrl = Some directoryProfilePicUrl
    }

    setupServicesWithDirectory context userRepo authService None [] (Some directoryIdentityData)
    |> ignore

    let middleware, _ = createMiddleware ()

    do! middleware.InvokeAsync(context)

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(Some directoryProfilePicUrl, addedUsers.[0].State.ProfilePicUrl)
}

[<Fact>]
let ``Uses configured email header`` () : Task = task {
    let email = "newuser@example.com"

    let context = createTestHttpContext () |> fun c -> addHeader c "X-User-Email" email

    let userRepo = MockUserRepository(Map.empty, Ok(), Ok())
    let authService = MockAuthorizationService(Ok())

    setupServices context userRepo authService None [ "Auth:IAP:EmailHeader", "X-User-Email" ]
    |> ignore

    let middleware, wasNextCalled = createMiddleware ()

    do! middleware.InvokeAsync(context)

    Assert.Equal(200, context.Response.StatusCode)
    Assert.True(wasNextCalled ())

    let addedUsers = userRepo.GetAddedUsers()
    Assert.Single(addedUsers) |> ignore
    Assert.Equal(email, addedUsers.[0].State.Email)
}