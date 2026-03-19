namespace Freetool.Infrastructure.Database.Repositories

open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Database

type RunRepository(context: FreetoolDbContext, eventRepository: IEventRepository) =

    interface IRunRepository with

        member _.GetByIdAsync(runId: RunId) : Task<ValidatedRun option> = task {
            let! runData = context.Runs.FirstOrDefaultAsync(fun r -> r.Id = runId)

            return runData |> Option.ofObj |> Option.map (fun data -> Run.fromData data)
        }

        member _.GetByAppIdAsync (appId: AppId) (skip: int) (take: int) : Task<ValidatedRun list> = task {
            let! runDatas =
                context.Runs
                    .Where(fun r -> r.AppId = appId)
                    .OrderByDescending(fun r -> r.CreatedAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync()

            return runDatas |> Seq.map (fun data -> Run.fromData data) |> Seq.toList
        }

        member _.GetByStatusAsync (status: RunStatus) (skip: int) (take: int) : Task<ValidatedRun list> = task {
            let! runDatas =
                context.Runs
                    .Where(fun r -> r.Status = status)
                    .OrderByDescending(fun r -> r.CreatedAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync()

            return runDatas |> Seq.map (fun data -> Run.fromData data) |> Seq.toList
        }

        member _.GetByAppIdAndStatusAsync
            (appId: AppId)
            (status: RunStatus)
            (skip: int)
            (take: int)
            : Task<ValidatedRun list> =
            task {
                let! runDatas =
                    context.Runs
                        .Where(fun r -> r.AppId = appId && r.Status = status)
                        .OrderByDescending(fun r -> r.CreatedAt)
                        .Skip(skip)
                        .Take(take)
                        .ToListAsync()

                return runDatas |> Seq.map (fun data -> Run.fromData data) |> Seq.toList
            }

        member _.AddAsync(run: ValidatedRun) : Task<Result<unit, DomainError>> = task {
            try
                // Add domain entity directly to context
                context.Runs.Add(run.State) |> ignore

                // Save domain events
                let events = Run.getUncommittedEvents run

                for event in events do
                    do! eventRepository.SaveEventAsync event

                // Save changes to database
                let! _ = context.SaveChangesAsync()
                return Ok()
            with ex ->
                return Error(InvalidOperation $"Failed to save run: {ex.Message}")
        }

        member _.UpdateAsync(run: ValidatedRun) : Task<Result<unit, DomainError>> = task {
            try
                let runIdObj = Run.getId run

                let! existingData = context.Runs.FirstOrDefaultAsync(fun r -> r.Id = runIdObj)
                let existingDataOption = Option.ofObj existingData

                match existingDataOption with
                | None -> return Error(NotFound "Run not found")
                | Some existingEntity ->
                    // Update the already-tracked entity to avoid tracking conflicts
                    context.Entry(existingEntity).CurrentValues.SetValues(run.State)

                    // Save domain events
                    let events = Run.getUncommittedEvents run

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    // Save changes to database
                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with ex ->
                return Error(InvalidOperation $"Failed to update run: {ex.Message}")
        }

        member _.ExistsAsync(runId: RunId) : Task<bool> = task { return! context.Runs.AnyAsync(fun r -> r.Id = runId) }

        member _.GetCountAsync() : Task<int> = task { return! context.Runs.CountAsync() }

        member _.GetCountByAppIdAsync(appId: AppId) : Task<int> = task {
            return! context.Runs.CountAsync(fun r -> r.AppId = appId)
        }

        member _.GetCountByStatusAsync(status: RunStatus) : Task<int> = task {
            return! context.Runs.CountAsync(fun r -> r.Status = status)
        }

        member _.GetCountByAppIdAndStatusAsync (appId: AppId) (status: RunStatus) : Task<int> = task {
            return! context.Runs.CountAsync(fun r -> r.AppId = appId && r.Status = status)
        }