module Freetool.Domain.Tests.BusinessRulesTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.BusinessRules

[<Fact>]
let ``checkAppToResourceConflicts should return Ok when no apps provided`` () =
    // Arrange
    let apps = []
    let newUrlParams = Some [ ("param1", "value1") ]
    let newHeaders = Some [ ("header1", "value1") ]
    let newBody = Some [ ("body1", "value1") ]

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when no apps provided")

[<Fact>]
let ``checkAppToResourceConflicts should return Ok when no conflicts`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = [ ("existing", "value") ]
            Headers = [ ("X-Custom", "header") ]
            Body = [ ("field", "data") ]
        }
    ]

    let newUrlParams = Some [ ("new_param", "value1") ]
    let newHeaders = Some [ ("New-Header", "value1") ]
    let newBody = Some [ ("new_field", "value1") ]

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when no conflicts")

[<Fact>]
let ``checkAppToResourceConflicts should return Error when URL parameter conflicts`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = [ ("conflicting_param", "app_value") ]
            Headers = []
            Body = []
        }
    ]

    let newUrlParams = Some [ ("conflicting_param", "resource_value") ]
    let newHeaders = None
    let newBody = None

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("Resource cannot override existing App values", msg)
        Assert.Contains("App app1 URL parameters: conflicting_param", msg)
    | Ok _ -> Assert.True(false, "Expected error for URL parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkAppToResourceConflicts should return Error when header conflicts`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = []
            Headers = [ ("Content-Type", "application/json") ]
            Body = []
        }
    ]

    let newUrlParams = None
    let newHeaders = Some [ ("Content-Type", "application/xml") ]
    let newBody = None

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("Resource cannot override existing App values", msg)
        Assert.Contains("App app1 Headers: Content-Type", msg)
    | Ok _ -> Assert.True(false, "Expected error for header conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkAppToResourceConflicts should return Error when body parameter conflicts`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = []
            Headers = []
            Body = [ ("client_id", "12345") ]
        }
    ]

    let newUrlParams = None
    let newHeaders = None
    let newBody = Some [ ("client_id", "67890") ]

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("Resource cannot override existing App values", msg)
        Assert.Contains("App app1 Body parameters: client_id", msg)
    | Ok _ -> Assert.True(false, "Expected error for body parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkAppToResourceConflicts should combine conflicts from multiple apps`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = [ ("param1", "value1") ]
            Headers = []
            Body = []
        }
        {
            AppId = "app2"
            UrlParameters = [ ("param2", "value2") ]
            Headers = [ ("X-Custom", "header") ]
            Body = []
        }
    ]

    let newUrlParams = Some [ ("param1", "new_value1"); ("param2", "new_value2") ]
    let newHeaders = Some [ ("X-Custom", "new_header") ]
    let newBody = None

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("Resource cannot override existing App values", msg)
        Assert.Contains("param1", msg)
        Assert.Contains("param2", msg)
        Assert.Contains("X-Custom", msg)
    | Ok _ -> Assert.True(false, "Expected error for multiple conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkResourceToAppConflicts should return Ok when no conflicts`` () =
    // Arrange
    let resource = {
        UrlParameters = [ ("existing", "value") ]
        Headers = [ ("X-Custom", "header") ]
        Body = [ ("field", "data") ]
    }

    let newUrlParams = Some [ ("new_param", "value1") ]
    let newHeaders = Some [ ("New-Header", "value1") ]
    let newBody = Some [ ("new_field", "value1") ]

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when no conflicts")

[<Fact>]
let ``checkResourceToAppConflicts should return Error when URL parameter conflicts`` () =
    // Arrange
    let resource = {
        UrlParameters = [ ("conflicting_param", "resource_value") ]
        Headers = []
        Body = []
    }

    let newUrlParams = Some [ ("conflicting_param", "app_value") ]
    let newHeaders = None
    let newBody = None

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("URL parameters: conflicting_param", msg)
    | Ok _ -> Assert.True(false, "Expected error for URL parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkResourceToAppConflicts should return Error when header conflicts`` () =
    // Arrange
    let resource = {
        UrlParameters = []
        Headers = [ ("Authorization", "Bearer token") ]
        Body = []
    }

    let newUrlParams = None
    let newHeaders = Some [ ("Authorization", "Basic auth") ]
    let newBody = None

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Headers: Authorization", msg)
    | Ok _ -> Assert.True(false, "Expected error for header conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkResourceToAppConflicts should return Error when body parameter conflicts`` () =
    // Arrange
    let resource = {
        UrlParameters = []
        Headers = []
        Body = [ ("api_key", "secret123") ]
    }

    let newUrlParams = None
    let newHeaders = None
    let newBody = Some [ ("api_key", "newsecret456") ]

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("Body parameters: api_key", msg)
    | Ok _ -> Assert.True(false, "Expected error for body parameter conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkResourceToAppConflicts should combine multiple conflict types`` () =
    // Arrange
    let resource = {
        UrlParameters = [ ("param1", "value1") ]
        Headers = [ ("X-Custom", "header") ]
        Body = [ ("field1", "data1") ]
    }

    let newUrlParams = Some [ ("param1", "new_value1") ]
    let newHeaders = Some [ ("X-Custom", "new_header") ]
    let newBody = Some [ ("field1", "new_data1") ]

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Error(InvalidOperation msg) ->
        Assert.Contains("App cannot override existing Resource values", msg)
        Assert.Contains("param1", msg)
        Assert.Contains("X-Custom", msg)
        Assert.Contains("field1", msg)
    | Ok _ -> Assert.True(false, "Expected error for multiple conflicts")
    | Error other -> Assert.True(false, $"Expected InvalidOperation but got: {other}")

[<Fact>]
let ``checkResourceToAppConflicts should return Ok when new parameters are None`` () =
    // Arrange
    let resource = {
        UrlParameters = [ ("param1", "value1") ]
        Headers = [ ("X-Custom", "header") ]
        Body = [ ("field1", "data1") ]
    }

    let newUrlParams = None
    let newHeaders = None
    let newBody = None

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when new parameters are None")

[<Fact>]
let ``checkAppToResourceConflicts should return Ok when new parameters are None`` () =
    // Arrange
    let apps = [
        {
            AppId = "app1"
            UrlParameters = [ ("param1", "value1") ]
            Headers = [ ("X-Custom", "header") ]
            Body = [ ("field1", "data1") ]
        }
    ]

    let newUrlParams = None
    let newHeaders = None
    let newBody = None

    // Act
    let result =
        BusinessRules.checkAppToResourceConflicts apps newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when new parameters are None")

[<Fact>]
let ``checkResourceToAppConflicts should return Ok when resource has empty parameters`` () =
    // Arrange
    let resource = {
        UrlParameters = []
        Headers = []
        Body = []
    }

    let newUrlParams = Some [ ("param1", "value1") ]
    let newHeaders = Some [ ("X-Custom", "header") ]
    let newBody = Some [ ("field1", "data1") ]

    // Act
    let result =
        BusinessRules.checkResourceToAppConflicts resource newUrlParams newHeaders newBody

    // Assert
    match result with
    | Ok() -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected Ok when resource has empty parameters")