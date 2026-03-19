module Freetool.Domain.Tests.HttpExecutionServiceTests

open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.Services

// Mock HttpMessageHandler for testing
type MockHttpMessageHandler(responseContent: string, statusCode: HttpStatusCode) =
    inherit HttpMessageHandler()

    override _.SendAsync
        (request: HttpRequestMessage, cancellationToken: CancellationToken)
        : Task<HttpResponseMessage> =
        task {
            let response = new HttpResponseMessage(statusCode)
            response.Content <- new StringContent(responseContent, Encoding.UTF8, "application/json")
            return response
        }

type CaptureRequestMessageHandler(statusCode: HttpStatusCode) =
    inherit HttpMessageHandler()

    let mutable capturedBody: string option = None

    member _.CapturedBody = capturedBody

    override _.SendAsync
        (request: HttpRequestMessage, cancellationToken: CancellationToken)
        : Task<HttpResponseMessage> =
        task {
            if not (isNull request.Content) then
                let! body = request.Content.ReadAsStringAsync(cancellationToken)
                capturedBody <- Some body

            let response = new HttpResponseMessage(statusCode)
            response.Content <- new StringContent("ok", Encoding.UTF8, "application/json")
            return response
        }

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should build correct URL with parameters`` () = task {
    // Arrange
    let mockHandler = new MockHttpMessageHandler("test response", HttpStatusCode.OK)
    use httpClient = new HttpClient(mockHandler)

    let request = {
        BaseUrl = "https://api.example.com/users/123"
        UrlParameters = [ ("limit", "10"); ("offset", "5") ]
        Headers = [ ("Authorization", "Bearer token123") ]
        Body = [ ("email", "test@example.com") ]
        HttpMethod = "GET"
        UseJsonBody = false
    }

    // Act
    let! result = HttpExecutionService.executeRequestWithClient httpClient request

    // Assert
    match result with
    | Ok response -> Assert.Equal("test response", response)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")
}

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should handle different HTTP methods`` () = task {
    let httpMethods = [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH" ]

    for httpMethod in httpMethods do
        // Arrange
        let mockHandler =
            new MockHttpMessageHandler($"Response for {httpMethod}", HttpStatusCode.OK)

        use httpClient = new HttpClient(mockHandler)

        let request = {
            BaseUrl = "https://api.example.com/test"
            UrlParameters = []
            Headers = []
            Body = []
            HttpMethod = httpMethod
            UseJsonBody = false
        }

        // Act
        let! result = HttpExecutionService.executeRequestWithClient httpClient request

        // Assert
        match result with
        | Ok response -> Assert.Contains($"Response for {httpMethod}", response)
        | Error error -> Assert.True(false, $"Expected success for {httpMethod} but got error: {error}")
}

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should handle HTTP error responses`` () = task {
    // Arrange
    let mockHandler = new MockHttpMessageHandler("Not Found", HttpStatusCode.NotFound)
    use httpClient = new HttpClient(mockHandler)

    let request = {
        BaseUrl = "https://api.example.com/nonexistent"
        UrlParameters = []
        Headers = []
        Body = []
        HttpMethod = "GET"
        UseJsonBody = false
    }

    // Act
    let! result = HttpExecutionService.executeRequestWithClient httpClient request

    // Assert
    match result with
    | Error(InvalidOperation message) ->
        Assert.Contains("404", message)
        Assert.Contains("Not Found", message)
    | Ok response -> Assert.True(false, $"Expected error but got success: {response}")
    | Error error -> Assert.True(false, $"Expected InvalidOperation but got: {error}")
}

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should handle POST with body parameters`` () = task {
    // Arrange
    let mockHandler = new MockHttpMessageHandler("Created", HttpStatusCode.Created)
    use httpClient = new HttpClient(mockHandler)

    let request = {
        BaseUrl = "https://api.example.com/users"
        UrlParameters = []
        Headers = [ ("Content-Type", "application/x-www-form-urlencoded") ]
        Body = [ ("name", "John Doe"); ("email", "john@example.com") ]
        HttpMethod = "POST"
        UseJsonBody = false
    }

    // Act
    let! result = HttpExecutionService.executeRequestWithClient httpClient request

    // Assert
    match result with
    | Ok response -> Assert.Equal("Created", response)
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")
}

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should preserve JSON primitive types in request body`` () = task {
    // Arrange
    let captureHandler = new CaptureRequestMessageHandler(HttpStatusCode.OK)
    use httpClient = new HttpClient(captureHandler)

    let request = {
        BaseUrl = "https://api.example.com/subscriptions"
        UrlParameters = []
        Headers = []
        Body = [
            ("cancelImmediately", "true")
            ("count", "3")
            ("refundBehavior", "none")
            ("metadata", """{"source":"ui"}""")
        ]
        HttpMethod = "DELETE"
        UseJsonBody = true
    }

    // Act
    let! _ = HttpExecutionService.executeRequestWithClient httpClient request

    // Assert
    match captureHandler.CapturedBody with
    | None -> Assert.True(false, "Expected request body to be sent")
    | Some body ->
        use doc = JsonDocument.Parse(body)
        let root = doc.RootElement

        Assert.Equal(JsonValueKind.True, root.GetProperty("cancelImmediately").ValueKind)
        Assert.Equal(3, root.GetProperty("count").GetInt32())
        Assert.Equal("none", root.GetProperty("refundBehavior").GetString())
        Assert.Equal(JsonValueKind.Object, root.GetProperty("metadata").ValueKind)
}

[<Fact>]
let ``HttpExecutionService executeRequestWithClient should omit undefined JSON body values`` () = task {
    // Arrange
    let captureHandler = new CaptureRequestMessageHandler(HttpStatusCode.OK)
    use httpClient = new HttpClient(captureHandler)

    let request = {
        BaseUrl = "https://api.example.com/subscriptions"
        UrlParameters = []
        Headers = []
        Body = [
            ("subscriptionId", "\"sub_123\"")
            ("recreateEmail", "undefined")
            ("nested", """{"line1":"Rua dos Tordos"}""")
        ]
        HttpMethod = "POST"
        UseJsonBody = true
    }

    // Act
    let! _ = HttpExecutionService.executeRequestWithClient httpClient request

    // Assert
    match captureHandler.CapturedBody with
    | None -> Assert.True(false, "Expected request body to be sent")
    | Some body ->
        use doc = JsonDocument.Parse(body)
        let root = doc.RootElement
        let mutable recreateEmailProp = Unchecked.defaultof<JsonElement>
        Assert.False(root.TryGetProperty("recreateEmail", &recreateEmailProp))
        Assert.Equal("sub_123", root.GetProperty("subscriptionId").GetString())
        Assert.Equal(JsonValueKind.Object, root.GetProperty("nested").ValueKind)
}