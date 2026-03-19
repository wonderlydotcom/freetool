module Freetool.Domain.Tests.UserTests

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

[<Fact>]
let ``User name update should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let email = Email.Create(Some "john@example.com") |> unwrapResult
    let user = User.create "John Doe" email None

    // Act
    let result = User.updateName actorUserId (Some "Jane Doe") user

    // Assert
    match result with
    | Ok updatedUser ->
        let events = User.getUncommittedEvents updatedUser
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with // The update event is the second one
        | :? UserUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | NameChanged(oldValue, newValue) ->
                Assert.Equal("John Doe", oldValue)
                Assert.Equal("Jane Doe", newValue)
            | _ -> Assert.True(false, "Expected NameChanged event")
        | _ -> Assert.True(false, "Expected UserUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``User creation should generate UserCreatedEvent`` () =
    // Arrange
    let email = Email.Create(Some "john@example.com") |> unwrapResult

    // Act
    let user = User.create "John Doe" email None

    // Assert
    let events = User.getUncommittedEvents user
    Assert.Single(events) |> ignore

    match events.[0] with
    | :? UserCreatedEvent as event ->
        Assert.Equal("John Doe", event.Name)
        Assert.Equal(email, event.Email)
        Assert.Equal(None, event.ProfilePicUrl)
    | _ -> Assert.True(false, "Expected UserCreatedEvent")

[<Fact>]
let ``User email update should generate correct event`` () =
    // Arrange
    let actorUserId = UserId.FromGuid(Guid.NewGuid())
    let oldEmail = Email.Create(Some "john@example.com") |> unwrapResult
    let user = User.create "John Doe" oldEmail None

    // Act
    let result = User.updateEmail actorUserId "jane@example.com" user

    // Assert
    match result with
    | Ok updatedUser ->
        let events = User.getUncommittedEvents updatedUser
        Assert.Equal(2, events.Length) // Creation event + update event

        match events.[1] with
        | :? UserUpdatedEvent as event ->
            Assert.Single(event.Changes) |> ignore

            match event.Changes.[0] with
            | EmailChanged(oldValue, newValue) ->
                Assert.Equal(oldEmail.Value, oldValue.Value)
                Assert.Equal("jane@example.com", newValue.Value)
            | _ -> Assert.True(false, "Expected EmailChanged event")
        | _ -> Assert.True(false, "Expected UserUpdatedEvent")
    | Error error -> Assert.True(false, $"Expected success but got error: {error}")

[<Fact>]
let ``User validation should reject empty name`` () =
    // Arrange
    let email = Email.Create(Some "john@example.com") |> unwrapResult
    let user = User.create "" email None

    // Act
    let result = User.validate user

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("User name cannot be empty", msg)
    | _ -> Assert.True(false, "Expected validation error for empty name")

[<Fact>]
let ``User validation should reject name longer than 100 characters`` () =
    // Arrange
    let longName = String.replicate 101 "a"
    let email = Email.Create(Some "john@example.com") |> unwrapResult
    let user = User.create longName email None

    // Act
    let result = User.validate user

    // Assert
    match result with
    | Error(ValidationError msg) -> Assert.Equal("User name cannot exceed 100 characters", msg)
    | _ -> Assert.True(false, "Expected validation error for long name")