module Freetool.Application.Tests.RunMapperTests

open System
open Xunit
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Mappers

[<Fact>]
let ``runInputValueFromDto should convert DTO to domain model correctly`` () =
    // Arrange
    let dto = { Title = "userId"; Value = "12345" }

    // Act
    let result = RunMapper.runInputValueFromDto dto

    // Assert
    Assert.Equal("userId", result.Title)
    Assert.Equal("12345", result.Value)

[<Fact>]
let ``runInputValueToDto should convert domain model to DTO correctly`` () =
    // Arrange
    let inputValue: RunInputValue = {
        Title = "email"
        Value = "test@example.com"
    }

    // Act
    let result = RunMapper.runInputValueToDto inputValue

    // Assert
    Assert.Equal("email", result.Title)
    Assert.Equal("test@example.com", result.Value)

[<Fact>]
let ``fromCreateDto should convert CreateRunDto to domain input values`` () =
    // Arrange
    let dto = {
        InputValues = [
            { Title = "userId"; Value = "123" }
            {
                Title = "email"
                Value = "test@example.com"
            }
        ]
        DynamicBody = None
    }

    // Act
    let result = RunMapper.fromCreateDto dto

    // Assert
    Assert.Equal(2, result.Length)
    Assert.Equal("userId", result.[0].Title)
    Assert.Equal("123", result.[0].Value)
    Assert.Equal("email", result.[1].Title)
    Assert.Equal("test@example.com", result.[1].Value)

[<Fact>]
let ``executableHttpRequestToDto should convert domain model to DTO correctly`` () =
    // Arrange
    let request: ExecutableHttpRequest = {
        BaseUrl = "https://api.example.com/users/123"
        UrlParameters = [ "limit", "10"; "offset", "0" ]
        Headers = [ "Authorization", "Bearer token"; "Content-Type", "application/json" ]
        Body = [ "email", "test@example.com"; "name", "John Doe" ]
        HttpMethod = "POST"
        UseJsonBody = false
    }

    // Act
    let result = RunMapper.executableHttpRequestToDto request

    // Assert
    Assert.Equal("https://api.example.com/users/123", result.BaseUrl)
    Assert.Equal("POST", result.HttpMethod)

    Assert.Equal(2, result.UrlParameters.Length)
    Assert.Contains({ Key = "limit"; Value = "10" }, result.UrlParameters)
    Assert.Contains({ Key = "offset"; Value = "0" }, result.UrlParameters)

    Assert.Equal(2, result.Headers.Length)

    Assert.Contains(
        {
            Key = "Authorization"
            Value = "Bearer token"
        },
        result.Headers
    )

    Assert.Contains(
        {
            Key = "Content-Type"
            Value = "application/json"
        },
        result.Headers
    )

    Assert.Equal(2, result.Body.Length)

    Assert.Contains(
        {
            Key = "email"
            Value = "test@example.com"
        },
        result.Body
    )

    Assert.Contains({ Key = "name"; Value = "John Doe" }, result.Body)

// Helper function to create a test run
let createTestRun () =
    let appId = AppId.NewId()

    let inputValues: RunInputValue list = [
        { Title = "userId"; Value = "123" }
        {
            Title = "email"
            Value = "test@example.com"
        }
    ]

    let executableRequest: ExecutableHttpRequest = {
        BaseUrl = "https://api.example.com/users/123"
        UrlParameters = [ "limit", "10" ]
        Headers = [ "Authorization", "Bearer token" ]
        Body = [ "email", "test@example.com" ]
        HttpMethod = "GET"
        UseJsonBody = false
    }

    let runData: RunData = {
        Id = RunId.NewId()
        AppId = appId
        Status = Success
        InputValues = inputValues
        ExecutableRequest = Some executableRequest
        ExecutedSql = None
        Response = Some """{"id": 123, "name": "John Doe"}"""
        ErrorMessage = None
        StartedAt = Some(DateTime(2024, 1, 15, 10, 30, 0))
        CompletedAt = Some(DateTime(2024, 1, 15, 10, 30, 5))
        CreatedAt = DateTime(2024, 1, 15, 10, 29, 0)
        IsDeleted = false
    }

    Run.fromData runData