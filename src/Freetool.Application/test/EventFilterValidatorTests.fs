module Freetool.Application.Tests.EventFilterValidatorTests

open System
open Xunit
open Freetool.Application.DTOs

[<Fact>]
let ``validate keeps large skip values`` () =
    let filterDto: EventFilterDTO = {
        UserId = None
        EventType = None
        EntityType = None
        FromDate = None
        ToDate = None
        Skip = Some 2000
        Take = Some 50
    }

    let result = EventFilterValidator.validate filterDto

    match result with
    | Ok filter ->
        Assert.Equal(2000, filter.Skip)
        Assert.Equal(50, filter.Take)
    | Error errors -> failwith $"Expected Ok but got Error: {errors}"

[<Fact>]
let ``validate defaults skip and take when not provided`` () =
    let filterDto: EventFilterDTO = {
        UserId = None
        EventType = None
        EntityType = None
        FromDate = None
        ToDate = None
        Skip = None
        Take = None
    }

    let result = EventFilterValidator.validate filterDto

    match result with
    | Ok filter ->
        Assert.Equal(0, filter.Skip)
        Assert.Equal(50, filter.Take)
    | Error errors -> failwith $"Expected Ok but got Error: {errors}"

[<Fact>]
let ``validate rejects negative skip`` () =
    let filterDto: EventFilterDTO = {
        UserId = None
        EventType = None
        EntityType = None
        FromDate = None
        ToDate = None
        Skip = Some -1
        Take = Some 50
    }

    let result = EventFilterValidator.validate filterDto

    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error errors -> Assert.Contains("Skip must be greater than or equal to 0", errors)

[<Fact>]
let ``validate rejects take over max`` () =
    let filterDto: EventFilterDTO = {
        UserId = None
        EventType = None
        EntityType = None
        FromDate = None
        ToDate = None
        Skip = Some 0
        Take = Some 101
    }

    let result = EventFilterValidator.validate filterDto

    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error errors -> Assert.Contains("Take must be between 0 and 100", errors)

[<Fact>]
let ``validateAppFilter defaults includeRunEvents to true`` () =
    let dto: AppEventFilterDTO = {
        AppId = Guid.NewGuid().ToString()
        FromDate = None
        ToDate = None
        Skip = None
        Take = None
        IncludeRunEvents = None
    }

    let result = EventFilterValidator.validateAppFilter dto

    match result with
    | Ok filter -> Assert.True(filter.IncludeRunEvents)
    | Error errors -> failwith $"Expected Ok but got Error: {errors}"

[<Fact>]
let ``validateAppFilter rejects invalid app id`` () =
    let dto: AppEventFilterDTO = {
        AppId = "bad-id"
        FromDate = None
        ToDate = None
        Skip = Some 0
        Take = Some 50
        IncludeRunEvents = Some true
    }

    let result = EventFilterValidator.validateAppFilter dto

    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error errors -> Assert.Contains("Invalid AppId format - must be a valid GUID", errors)

[<Fact>]
let ``validateUserFilter parses user id and pagination`` () =
    let userId = Guid.NewGuid().ToString()

    let dto: UserEventFilterDTO = {
        UserId = userId
        FromDate = None
        ToDate = None
        Skip = Some 8
        Take = Some 20
    }

    let result = EventFilterValidator.validateUserFilter dto

    match result with
    | Ok filter ->
        Assert.Equal(userId, filter.UserId.Value.ToString())
        Assert.Equal(8, filter.Skip)
        Assert.Equal(20, filter.Take)
    | Error errors -> failwith $"Expected Ok but got Error: {errors}"

[<Fact>]
let ``validateUserFilter rejects invalid date range`` () =
    let dto: UserEventFilterDTO = {
        UserId = Guid.NewGuid().ToString()
        FromDate = Some(DateTime(2026, 2, 2))
        ToDate = Some(DateTime(2026, 2, 1))
        Skip = None
        Take = None
    }

    let result = EventFilterValidator.validateUserFilter dto

    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error errors -> Assert.Contains("FromDate must be less than or equal to ToDate", errors)