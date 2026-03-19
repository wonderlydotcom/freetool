module Freetool.Domain.Tests.RunStatusTests

open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects

[<Fact>]
let ``RunStatus creation from valid strings should succeed`` () =
    // Test all valid status strings
    let validStatuses = [
        ("pending", Pending)
        ("running", Running)
        ("success", Success)
        ("failure", Failure)
        ("invalid_configuration", InvalidConfiguration)
    ]

    for (statusString, expectedStatus) in validStatuses do
        match RunStatus.Create statusString with
        | Ok status -> Assert.Equal(expectedStatus, status)
        | Error error -> Assert.True(false, $"Expected success for '{statusString}' but got error: {error}")

[<Fact>]
let ``RunStatus creation should be case insensitive`` () =
    // Test case insensitivity
    let caseVariations = [ "PENDING"; "Running"; "SUCCESS"; "failure"; "Invalid_Configuration" ]

    for statusString in caseVariations do
        match RunStatus.Create statusString with
        | Ok _ -> Assert.True(true) // Success expected
        | Error error -> Assert.True(false, $"Expected success for '{statusString}' but got error: {error}")

[<Fact>]
let ``RunStatus creation from invalid string should fail`` () =
    let invalidStatuses = [ "invalid"; "completed"; "cancelled"; ""; "pending_approval" ]

    for statusString in invalidStatuses do
        match RunStatus.Create statusString with
        | Ok status -> Assert.True(false, $"Expected error for '{statusString}' but got success: {status}")
        | Error(ValidationError message) ->
            Assert.Contains("Invalid run status", message)
            Assert.Contains(statusString, message)
        | Error error -> Assert.True(false, $"Expected ValidationError but got: {error}")

[<Fact>]
let ``RunStatus ToString should return correct string representation`` () =
    let statusMappings = [
        (Pending, "Pending")
        (Running, "Running")
        (Success, "Success")
        (Failure, "Failure")
        (InvalidConfiguration, "InvalidConfiguration")
    ]

    for (status, expectedString) in statusMappings do
        Assert.Equal(expectedString, status.ToString())