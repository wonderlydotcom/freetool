module Freetool.Application.Tests.DashboardHandlerRuntimeTests

open System
open System.Threading.Tasks
open Xunit
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Commands
open Freetool.Application.Handlers
open Freetool.Application.Interfaces
open Freetool.Application.DTOs

type TestDashboardRepository(dashboard: ValidatedDashboard option) =
    interface IDashboardRepository with
        member _.GetByIdAsync(_dashboardId: DashboardId) = task { return dashboard }
        member _.GetByFolderIdAsync _ _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ _ = task { return Ok() }
        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetCountAsync() = task { return 0 }

type TestRunRepository(prepareRun: ValidatedRun option) =
    interface IRunRepository with
        member _.GetByIdAsync(_runId: RunId) = task { return prepareRun }
        member _.GetByAppIdAsync _ _ _ = task { return [] }
        member _.GetByStatusAsync _ _ _ = task { return [] }
        member _.GetByAppIdAndStatusAsync _ _ _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountByAppIdAsync _ = task { return 0 }
        member _.GetCountByStatusAsync _ = task { return 0 }
        member _.GetCountByAppIdAndStatusAsync _ _ = task { return 0 }

type NoopAppRepository() =
    interface IAppRepository with
        member _.GetByIdAsync _ = task { return None }
        member _.GetByNameAndFolderIdAsync _ _ = task { return None }
        member _.GetByFolderIdAsync _ _ _ = task { return [] }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceIdsAsync _ _ _ = task { return [] }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.ExistsByNameAndFolderIdAsync _ _ = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetCountByFolderIdAsync _ = task { return 0 }
        member _.GetCountBySpaceIdsAsync _ = task { return 0 }
        member _.GetByResourceIdAsync _ = task { return [] }
        member _.GetDeletedByFolderIdsAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

type NoopResourceRepository() =
    interface IResourceRepository with
        member _.GetByIdAsync _ = task { return None }
        member _.GetAllAsync _ _ = task { return [] }
        member _.GetBySpaceAsync _ _ _ = task { return [] }
        member _.GetCountBySpaceAsync _ = task { return 0 }
        member _.AddAsync _ = task { return Ok() }
        member _.UpdateAsync _ = task { return Ok() }
        member _.DeleteAsync _ = task { return Ok() }
        member _.ExistsAsync _ = task { return false }
        member _.ExistsByNameAsync _ = task { return false }
        member _.GetCountAsync() = task { return 0 }
        member _.GetDeletedBySpaceAsync _ = task { return [] }
        member _.GetDeletedByIdAsync _ = task { return None }
        member _.RestoreAsync _ = task { return Ok() }
        member _.CheckNameConflictAsync _ _ = task { return false }

type NoopSqlExecutionService() =
    interface ISqlExecutionService with
        member _.ExecuteQueryAsync _ _ = task { return Error(InvalidOperation "not used") }

type NoopEventRepository() =
    interface IEventRepository with
        member _.SaveEventAsync _ = task { return () }
        member _.CommitAsync() = task { return () }

        member _.GetEventsAsync _ = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 50
            }
        }

        member _.GetEventsByAppIdAsync _ = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 50
            }
        }

        member _.GetEventsByDashboardIdAsync _ = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 50
            }
        }

        member _.GetEventsByUserIdAsync _ = task {
            return {
                Items = []
                TotalCount = 0
                Skip = 0
                Take = 50
            }
        }

let private actorUserId = UserId.NewId()
let private folderId = FolderId.NewId()
let private prepareAppId = AppId.NewId()
let private actionAppId = AppId.NewId()

let private actionId =
    ActionId.Create(Some "approve") |> Result.toOption |> Option.get

let private runtimeConfig =
    $"{{\"actions\":[{{\"id\":\"{actionId.Value}\",\"appId\":\"{actionAppId.Value}\"}}],\"bindings\":[]}}"

let private createDashboardWithPrepareApp () =
    match Dashboard.create actorUserId "Test Dashboard" folderId (Some prepareAppId) runtimeConfig with
    | Ok dashboard -> dashboard
    | Error error -> failwith $"Failed to build test dashboard: {error}"

let private createRunWithStatus (status: RunStatus) =
    let runData: RunData = {
        Id = RunId.NewId()
        AppId = prepareAppId
        Status = status
        InputValues = []
        ExecutableRequest = None
        ExecutedSql = None
        Response = Some "{}"
        ErrorMessage = if status = RunStatus.Success then None else Some "failed"
        StartedAt = None
        CompletedAt = None
        CreatedAt = DateTime.UtcNow
        IsDeleted = false
    }

    Run.fromData runData

let private runActionCommand
    (dashboard: ValidatedDashboard option)
    (prepareRun: ValidatedRun option)
    (dto: RunDashboardActionDto)
    =
    let dashboardRepository = TestDashboardRepository(dashboard) :> IDashboardRepository
    let runRepository = TestRunRepository(prepareRun) :> IRunRepository
    let appRepository = NoopAppRepository() :> IAppRepository
    let resourceRepository = NoopResourceRepository() :> IResourceRepository
    let sqlExecutionService = NoopSqlExecutionService() :> ISqlExecutionService
    let eventRepository = NoopEventRepository() :> IEventRepository

    let currentUser: CurrentUser = {
        Id = actorUserId.Value.ToString()
        Email = "test@freetool.dev"
        FirstName = "Test"
        LastName = "User"
    }

    DashboardHandler.handleCommand
        dashboardRepository
        runRepository
        appRepository
        resourceRepository
        sqlExecutionService
        eventRepository
        (RunDashboardAction(actorUserId, DashboardId.NewId().Value.ToString(), actionId, currentUser, dto))

[<Fact>]
let ``RunDashboardAction rejects priorActionRunIds in v1`` () = task {
    let dto: RunDashboardActionDto = {
        PrepareRunId = None
        LoadInputs = []
        ActionInputs = []
        PriorActionRunIds =
            Some [
                {
                    Key = "step1"
                    Value = Guid.NewGuid().ToString()
                }
            ]
    }

    let! result = runActionCommand None None dto

    match result with
    | Error(ValidationError message) -> Assert.Contains("priorActionRunIds", message)
    | _ -> Assert.Fail("Expected ValidationError for priorActionRunIds")
}

[<Fact>]
let ``RunDashboardAction requires prepareRunId when prepare app is configured`` () = task {
    let dashboard = createDashboardWithPrepareApp ()

    let dto: RunDashboardActionDto = {
        PrepareRunId = None
        LoadInputs = []
        ActionInputs = []
        PriorActionRunIds = None
    }

    let! result = runActionCommand (Some dashboard) None dto

    match result with
    | Error(ValidationError message) -> Assert.Contains("prepareRunId is required", message)
    | _ -> Assert.Fail("Expected ValidationError for missing prepareRunId")
}

[<Fact>]
let ``RunDashboardAction requires successful prepare run`` () = task {
    let dashboard = createDashboardWithPrepareApp ()
    let prepareRun = createRunWithStatus RunStatus.Failure

    let dto: RunDashboardActionDto = {
        PrepareRunId = Some(prepareRun.State.Id.Value.ToString())
        LoadInputs = []
        ActionInputs = []
        PriorActionRunIds = None
    }

    let! result = runActionCommand (Some dashboard) (Some prepareRun) dto

    match result with
    | Error(ValidationError message) -> Assert.Contains("successful prepare run", message)
    | _ -> Assert.Fail("Expected ValidationError for unsuccessful prepare run")
}