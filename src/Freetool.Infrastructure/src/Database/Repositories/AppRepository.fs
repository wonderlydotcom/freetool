namespace Freetool.Infrastructure.Database.Repositories

open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.FSharp.Linq
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Database

type AppRepository(context: FreetoolDbContext, eventRepository: IEventRepository) =

    interface IAppRepository with

        member _.GetByIdAsync(appId: AppId) : Task<ValidatedApp option> = task {
            let! appData = context.Apps.FirstOrDefaultAsync(fun a -> a.Id = appId)

            return appData |> Option.ofObj |> Option.map (fun data -> App.fromData data)
        }

        member _.GetByNameAndFolderIdAsync (appName: AppName) (folderId: FolderId) : Task<ValidatedApp option> = task {
            let nameStr = appName.Value

            let! appData = context.Apps.FirstOrDefaultAsync(fun a -> a.Name = nameStr && a.FolderId = folderId)

            return appData |> Option.ofObj |> Option.map (fun data -> App.fromData data)
        }

        member _.GetByFolderIdAsync (folderId: FolderId) (skip: int) (take: int) : Task<ValidatedApp list> = task {
            let! appDatas =
                context.Apps
                    .Where(fun a -> a.FolderId = folderId)
                    .OrderBy(fun a -> a.CreatedAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync()

            return appDatas |> Seq.map (fun data -> App.fromData data) |> Seq.toList
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedApp list> = task {
            let! appDatas = context.Apps.OrderBy(fun a -> a.CreatedAt).Skip(skip).Take(take).ToListAsync()

            return appDatas |> Seq.map (fun data -> App.fromData data) |> Seq.toList
        }

        member _.GetBySpaceIdsAsync (spaceIds: SpaceId list) (skip: int) (take: int) : Task<ValidatedApp list> = task {
            if List.isEmpty spaceIds then
                return []
            else
                let spaceIdArray = spaceIds |> List.toArray

                // Get folder IDs that belong to the spaces first
                // Note: Compare SpaceId directly (not .Value) - EF Core's ValueConverter handles translation
                let! folderIds =
                    context.Folders.Where(fun f -> spaceIdArray.Contains(f.SpaceId)).Select(fun f -> f.Id).ToListAsync()

                let! appDatas =
                    context.Apps
                        .Where(fun a -> folderIds.Contains(a.FolderId))
                        .OrderBy(fun a -> a.CreatedAt)
                        .Skip(skip)
                        .Take(take)
                        .ToListAsync()

                return appDatas |> Seq.map (fun data -> App.fromData data) |> Seq.toList
        }

        member _.AddAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            try
                // 1. Save app to database
                context.Apps.Add app.State |> ignore

                // 2. Save events to audit log in SAME transaction
                let events = App.getUncommittedEvents app

                for event in events do
                    do! eventRepository.SaveEventAsync event

                // 3. Commit everything atomically
                let! _ = context.SaveChangesAsync()
                return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to add app: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.UpdateAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            try
                let appId = App.getId app
                let! existingData = context.Apps.FirstOrDefaultAsync(fun a -> a.Id = appId)

                match Option.ofObj existingData with
                | None -> return Error(NotFound "App not found")
                | Some existingEntity ->
                    // Update the already-tracked entity to avoid tracking conflicts
                    context.Entry(existingEntity).CurrentValues.SetValues(app.State)

                    // Save events to audit log in SAME transaction
                    let events = App.getUncommittedEvents app

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    // Save to the database
                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to update app: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.DeleteAsync (appId: AppId) (userId: UserId) : Task<Result<unit, DomainError>> = task {
            try
                let! existingData = context.Apps.FirstOrDefaultAsync(fun a -> a.Id = appId)

                match Option.ofObj existingData with
                | None -> return Error(NotFound "App not found")
                | Some existingEntity ->
                    // Create a ValidatedApp from the existing entity and mark it for deletion
                    let validatedApp = App.fromData existingEntity
                    let appWithDeleteEvent = App.markForDeletion userId validatedApp

                    // Update the already-tracked entity to avoid tracking conflicts
                    context.Entry(existingEntity).CurrentValues.SetValues {
                        appWithDeleteEvent.State with
                            IsDeleted = true
                            UpdatedAt = System.DateTime.UtcNow
                    }

                    // Save all uncommitted events
                    let events = App.getUncommittedEvents appWithDeleteEvent

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    // Save to the database
                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to delete app: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.ExistsAsync(appId: AppId) : Task<bool> = task { return! context.Apps.AnyAsync(fun a -> a.Id = appId) }

        member _.ExistsByNameAndFolderIdAsync (appName: AppName) (folderId: FolderId) : Task<bool> = task {
            let nameStr = appName.Value
            return! context.Apps.AnyAsync(fun a -> a.Name = nameStr && a.FolderId = folderId)
        }

        member _.GetCountAsync() : Task<int> = task { return! context.Apps.CountAsync() }

        member _.GetCountByFolderIdAsync(folderId: FolderId) : Task<int> = task {
            return! context.Apps.CountAsync(fun a -> a.FolderId = folderId)
        }

        member _.GetCountBySpaceIdsAsync(spaceIds: SpaceId list) : Task<int> = task {
            if List.isEmpty spaceIds then
                return 0
            else
                let spaceIdArray = spaceIds |> List.toArray

                // Get folder IDs that belong to the spaces first
                // Note: Compare SpaceId directly (not .Value) - EF Core's ValueConverter handles translation
                let! folderIds =
                    context.Folders.Where(fun f -> spaceIdArray.Contains(f.SpaceId)).Select(fun f -> f.Id).ToListAsync()

                return! context.Apps.Where(fun a -> folderIds.Contains(a.FolderId)).CountAsync()
        }

        member _.GetByResourceIdAsync(resourceId: ResourceId) : Task<ValidatedApp list> = task {
            let! appDatas = context.Apps.Where(fun a -> a.ResourceId = resourceId).ToListAsync()
            return appDatas |> Seq.map (fun data -> App.fromData data) |> Seq.toList
        }

        member _.GetDeletedByIdAsync(appId: AppId) : Task<ValidatedApp option> = task {
            let! appData =
                context.Apps.IgnoreQueryFilters().Where(fun a -> a.Id = appId && a.IsDeleted).FirstOrDefaultAsync()

            return appData |> Option.ofObj |> Option.map App.fromData
        }

        member _.GetDeletedByFolderIdsAsync(folderIds: FolderId list) : Task<ValidatedApp list> = task {
            let! appDatas =
                context.Apps
                    .IgnoreQueryFilters()
                    .Where(fun a -> folderIds.Contains(a.FolderId) && a.IsDeleted)
                    .OrderByDescending(fun a -> a.UpdatedAt)
                    .ToListAsync()

            return appDatas |> Seq.map App.fromData |> Seq.toList
        }

        member _.RestoreAsync(app: ValidatedApp) : Task<Result<unit, DomainError>> = task {
            try
                let appId = App.getId app

                let! existingData = context.Apps.IgnoreQueryFilters().FirstOrDefaultAsync(fun a -> a.Id = appId)

                match Option.ofObj existingData with
                | None -> return Error(NotFound "App not found in trash")
                | Some entity ->
                    context.Entry(entity).CurrentValues.SetValues(app.State)

                    let events = App.getUncommittedEvents app

                    for event in events do
                        do! eventRepository.SaveEventAsync event

                    let! _ = context.SaveChangesAsync()
                    return Ok()
            with
            | :? DbUpdateException as ex -> return Error(Conflict $"Failed to restore app: {ex.Message}")
            | ex -> return Error(InvalidOperation $"Database error: {ex.Message}")
        }

        member _.CheckNameConflictAsync (name: string) (folderId: FolderId) : Task<bool> = task {
            return! context.Apps.AnyAsync(fun a -> a.Name = name && a.FolderId = folderId)
        }