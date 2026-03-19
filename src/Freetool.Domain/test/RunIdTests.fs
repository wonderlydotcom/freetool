module Freetool.Domain.Tests.RunIdTests

open System
open Xunit
open Freetool.Domain.ValueObjects

[<Fact>]
let ``RunId NewId should generate valid GUID`` () =
    // Act
    let runId = RunId.NewId()

    // Assert
    Assert.NotEqual(Guid.Empty, runId.Value)
    Assert.NotEqual(RunId.NewId().Value, runId.Value) // Should be unique

[<Fact>]
let ``RunId FromGuid should create RunId with specified GUID`` () =
    // Arrange
    let testGuid = Guid.NewGuid()

    // Act
    let runId = RunId.FromGuid(testGuid)

    // Assert
    Assert.Equal(testGuid, runId.Value)

[<Fact>]
let ``RunId ToString should return GUID string representation`` () =
    // Arrange
    let testGuid = Guid.NewGuid()
    let runId = RunId.FromGuid(testGuid)

    // Act
    let result = runId.ToString()

    // Assert
    Assert.Equal(testGuid.ToString(), result)

[<Fact>]
let ``RunId Value property should return underlying GUID`` () =
    // Arrange
    let testGuid = Guid.NewGuid()
    let runId = RunId.FromGuid(testGuid)

    // Act & Assert
    Assert.Equal(testGuid, runId.Value)