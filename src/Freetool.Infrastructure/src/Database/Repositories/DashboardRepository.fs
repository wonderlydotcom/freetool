namespace Freetool.Infrastructure.Database.Repositories

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Database

type DashboardRepository(context: FreetoolDbContext, eventRepository: IEventRepository) =

    interface IDashboardRepository with
        member _.GetByIdAsync(dashboardId: DashboardId) : Task<ValidatedDashboard option> = task {
            let! dashboardData = context.Dashboards.FirstOrDefaultAsync(fun d -> d.Id = dashboardId)
            return dashboardData |> Option.ofObj |> Option.map Dashboard.fromData
        }

        member _.GetByFolderIdAsync (folderId: FolderId) (skip: int) (take: int) : Task<ValidatedDashboard list> = task {
            let! dashboardDatas =
                context.Dashboards
                    .Where(fun d -> d.FolderId = folderId)
                    .OrderBy(fun d -> d.CreatedAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync()

            return dashboardDatas |> Seq.map Dashboard.fromData |> Seq.toList
        }

        member _.GetBySpaceIdsAsync (spaceIds: SpaceId list) (skip: int) (take: int) : Task<ValidatedDashboard list> = task {
            if List.isEmpty spaceIds then
                return []
            else
                let spaceIdArray = spaceIds |> List.toArray

                let! folderIds =
                    context.Folders.Where(fun f -> spaceIdArray.Contains(f.SpaceId)).Select(fun f -> f.Id).ToListAsync()

                let! dashboardDatas =
                    context.Dashboards
                        .Where(fun d -> folderIds.Contains(d.FolderId))
                        .OrderBy(fun d -> d.CreatedAt)
                        .Skip(skip)
                        .Take(take)
                        .ToListAsync()

                return dashboardDatas |> Seq.map Dashboard.fromData |> Seq.toList
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedDashboard list> = task {
            let! dashboardDatas = context.Dashboards.OrderBy(fun d -> d.CreatedAt).Skip(skip).Take(take).ToListAsync()

            return dashboardDatas |> Seq.map Dashboard.fromData |> Seq.toList
        }

        member _.AddAsync(dashboard: ValidatedDashboard) : Task<Result<unit, DomainError>> = task {
            try
                context.Dashboards.Add dashboard.State |> ignore

                let events = Dashboard.getUncommittedEvents dashboard

                for event in events do
                    do! eventRepository.SaveEventAsync event

                let! _ = context.SaveChangesAsync()
                return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to add dashboard: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.UpdateAsync(dashboard: ValidatedDashboard) : Task<Result<unit, DomainError>> = task {
            try
                let dashboardId = Dashboard.getId dashboard
                let! existingData = context.Dashboards.FirstOrDefaultAsync(fun d -> d.Id = dashboardId)

                match Option.ofObj existingData with
                | None -> return Error(NotFound "Dashboard not found")
                | Some existingEntity ->
                    context.Entry(existingEntity).CurrentValues.SetValues(dashboard.State)

                    let events = Dashboard.getUncommittedEvents dashboard

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to update dashboard: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.DeleteAsync (dashboardId: DashboardId) (actorUserId: UserId) : Task<Result<unit, DomainError>> = task {
            try
                let! existingData = context.Dashboards.FirstOrDefaultAsync(fun d -> d.Id = dashboardId)

                match Option.ofObj existingData with
                | None -> return Error(NotFound "Dashboard not found")
                | Some existingEntity ->
                    let validatedDashboard = Dashboard.fromData existingEntity

                    let dashboardWithDeleteEvent =
                        Dashboard.markForDeletion actorUserId validatedDashboard

                    context.Entry(existingEntity).CurrentValues.SetValues {
                        dashboardWithDeleteEvent.State with
                            IsDeleted = true
                            UpdatedAt = DateTime.UtcNow
                    }

                    let events = Dashboard.getUncommittedEvents dashboardWithDeleteEvent

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to delete dashboard: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.ExistsByNameAndFolderIdAsync (dashboardName: DashboardName) (folderId: FolderId) : Task<bool> = task {
            return! context.Dashboards.AnyAsync(fun d -> d.Name = dashboardName && d.FolderId = folderId)
        }

        member _.GetCountByFolderIdAsync(folderId: FolderId) : Task<int> = task {
            return! context.Dashboards.CountAsync(fun d -> d.FolderId = folderId)
        }

        member _.GetCountBySpaceIdsAsync(spaceIds: SpaceId list) : Task<int> = task {
            if List.isEmpty spaceIds then
                return 0
            else
                let spaceIdArray = spaceIds |> List.toArray

                let! folderIds =
                    context.Folders.Where(fun f -> spaceIdArray.Contains(f.SpaceId)).Select(fun f -> f.Id).ToListAsync()

                return! context.Dashboards.Where(fun d -> folderIds.Contains(d.FolderId)).CountAsync()
        }

        member _.GetCountAsync() : Task<int> = task { return! context.Dashboards.CountAsync() }