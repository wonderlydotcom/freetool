namespace Freetool.Application.Handlers

open System
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Application.Commands
open Freetool.Application.Mappers
open Freetool.Application.DTOs
open Freetool.Domain.Services

module RunHandler =

    let handleCommand
        (runRepository: IRunRepository)
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (sqlExecutionService: ISqlExecutionService)
        (command: RunCommand)
        : Task<Result<RunCommandResult, DomainError>> =
        task {
            match command with
            | CreateRun(actorUserId, appId, currentUser, dto) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! appOption = appRepository.GetByIdAsync appIdObj

                    match appOption with
                    | None -> return Error(NotFound "App not found")
                    | Some app ->
                        let inputValues = RunMapper.fromCreateDto dto

                        match Run.createWithValidation actorUserId app inputValues with
                        | Error domainError -> return Error domainError
                        | Ok validatedRun ->
                            // Get the resource associated with the app
                            let resourceId = App.getResourceId app
                            let! resourceOption = resourceRepository.GetByIdAsync resourceId

                            match resourceOption with
                            | None ->
                                let runWithError =
                                    Run.markAsInvalidConfiguration
                                        actorUserId
                                        "Associated resource not found"
                                        validatedRun

                                match! runRepository.AddAsync runWithError with
                                | Error error -> return Error error
                                | Ok() -> return Ok(RunResult(runWithError.State))
                            | Some resource ->
                                match Resource.getResourceKind resource with
                                | ResourceKind.Http ->
                                    // Extract dynamic body from DTO and convert to tuple list
                                    let dynamicBody =
                                        dto.DynamicBody |> Option.map (List.map (fun kvp -> (kvp.Key, kvp.Value)))

                                    // Compose executable request with input substitution
                                    match
                                        Run.composeExecutableRequestFromAppAndResource
                                            validatedRun
                                            app
                                            resource
                                            currentUser
                                            dynamicBody
                                    with
                                    | Error err ->
                                        let runWithError =
                                            Run.markAsInvalidConfiguration actorUserId (err.ToString()) validatedRun

                                        match! runRepository.AddAsync runWithError with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(RunResult(runWithError.State))
                                    | Ok runWithExecutableRequest ->
                                        // Save the run first
                                        match! runRepository.AddAsync runWithExecutableRequest with
                                        | Error error -> return Error error
                                        | Ok() ->
                                            // Get fresh run from database to avoid event conflicts
                                            let runId = Run.getId runWithExecutableRequest
                                            let! freshRunOption = runRepository.GetByIdAsync runId

                                            match freshRunOption with
                                            | None -> return Error(NotFound "Run not found after save")
                                            | Some freshRun ->
                                                // Mark as running and execute the request in background
                                                let runningRun = Run.markAsRunning actorUserId freshRun

                                                // Execute the HTTP request (this would typically be done asynchronously)
                                                let executableRequest =
                                                    Run.getExecutableRequest runningRun |> Option.get

                                                let! executeResult =
                                                    HttpExecutionService.executeRequest executableRequest

                                                let finalRun =
                                                    match executeResult with
                                                    | Ok response -> Run.markAsSuccess actorUserId response runningRun
                                                    | Error err ->
                                                        Run.markAsFailure actorUserId (err.ToString()) runningRun

                                                // Update the run with final status
                                                match! runRepository.UpdateAsync finalRun with
                                                | Error error -> return Error error
                                                | Ok() -> return Ok(RunResult(finalRun.State))
                                | ResourceKind.Sql ->
                                    match Run.composeSqlQueryFromAppAndResource validatedRun app currentUser with
                                    | Error err ->
                                        let runWithError =
                                            Run.markAsInvalidConfiguration actorUserId (err.ToString()) validatedRun

                                        match! runRepository.AddAsync runWithError with
                                        | Error error -> return Error error
                                        | Ok() -> return Ok(RunResult(runWithError.State))
                                    | Ok sqlQuery ->
                                        let runWithExecutedSql = Run.setExecutedSql sqlQuery.Sql validatedRun

                                        match! runRepository.AddAsync runWithExecutedSql with
                                        | Error error -> return Error error
                                        | Ok() ->
                                            let runId = Run.getId runWithExecutedSql
                                            let! freshRunOption = runRepository.GetByIdAsync runId

                                            match freshRunOption with
                                            | None -> return Error(NotFound "Run not found after save")
                                            | Some freshRun ->
                                                let runningRun = Run.markAsRunning actorUserId freshRun

                                                let! executeResult =
                                                    sqlExecutionService.ExecuteQueryAsync resource.State sqlQuery

                                                let finalRun =
                                                    match executeResult with
                                                    | Ok response -> Run.markAsSuccess actorUserId response runningRun
                                                    | Error err ->
                                                        Run.markAsFailure actorUserId (err.ToString()) runningRun

                                                match! runRepository.UpdateAsync finalRun with
                                                | Error error -> return Error error
                                                | Ok() -> return Ok(RunResult(finalRun.State))

            | GetRunById runId ->
                match Guid.TryParse runId with
                | false, _ -> return Error(ValidationError "Invalid run ID format")
                | true, guid ->
                    let runIdObj = RunId.FromGuid guid
                    let! runOption = runRepository.GetByIdAsync runIdObj

                    match runOption with
                    | None -> return Error(NotFound "Run not found")
                    | Some run -> return Ok(RunResult(run.State))

            | GetRunsByAppId(appId, skip, take) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    let appIdObj = AppId.FromGuid guid
                    let! runs = runRepository.GetByAppIdAsync appIdObj skip take
                    let! totalCount = runRepository.GetCountByAppIdAsync appIdObj

                    let pagedResult = {
                        Items = runs |> List.map (fun run -> run.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(RunsResult pagedResult)

            | GetRunsByStatus(status, skip, take) ->
                match RunStatus.Create status with
                | Error err -> return Error err
                | Ok statusObj ->
                    let! runs = runRepository.GetByStatusAsync statusObj skip take
                    let! totalCount = runRepository.GetCountByStatusAsync statusObj

                    let pagedResult = {
                        Items = runs |> List.map (fun run -> run.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(RunsResult pagedResult)

            | GetRunsByAppIdAndStatus(appId, status, skip, take) ->
                match Guid.TryParse appId with
                | false, _ -> return Error(ValidationError "Invalid app ID format")
                | true, guid ->
                    match RunStatus.Create status with
                    | Error err -> return Error err
                    | Ok statusObj ->
                        let appIdObj = AppId.FromGuid guid
                        let! runs = runRepository.GetByAppIdAndStatusAsync appIdObj statusObj skip take
                        let! totalCount = runRepository.GetCountByAppIdAndStatusAsync appIdObj statusObj

                        let pagedResult = {
                            Items = runs |> List.map (fun run -> run.State)
                            TotalCount = totalCount
                            Skip = skip
                            Take = take
                        }

                        return Ok(RunsResult pagedResult)
        }