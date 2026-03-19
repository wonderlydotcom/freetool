module Freetool.Infrastructure.Tests.SpaceRepositoryTests

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Infrastructure.Database
open Freetool.Infrastructure.Database.Repositories

type NoOpEventRepository() =
    interface IEventRepository with
        member _.SaveEventAsync(_event: IDomainEvent) = Task.FromResult(())
        member _.CommitAsync() = Task.FromResult(())

        member _.GetEventsAsync(_filter: EventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 0
                }
            )

        member _.GetEventsByAppIdAsync(_filter: AppEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 0
                }
            )

        member _.GetEventsByDashboardIdAsync(_filter: DashboardEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 0
                }
            )

        member _.GetEventsByUserIdAsync(_filter: UserEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 0
                }
            )

let private createSqliteContext () =
    let connection = new SqliteConnection("Data Source=:memory:")
    connection.Open()

    let options =
        DbContextOptionsBuilder<FreetoolDbContext>().UseSqlite(connection).Options

    let context = new FreetoolDbContext(options)
    context.Database.EnsureCreated() |> ignore
    context, connection

let private createUserData (userId: UserId) (email: string) (name: string) = {
    Id = userId
    Name = name
    Email = email
    ProfilePicUrl = None
    CreatedAt = DateTime.UtcNow
    UpdatedAt = DateTime.UtcNow
    IsDeleted = false
    InvitedAt = None
}

let private unwrapOrFail value errorMessage =
    match value with
    | Ok result -> result
    | Error _ -> failwith errorMessage

[<Fact>]
let ``GetByNameAsync returns space when persisted SpaceData has null MemberIds`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection
    let repository = SpaceRepository(context, NoOpEventRepository()) :> ISpaceRepository

    let moderatorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    context.Users.Add(createUserData moderatorUserId "moderator@example.com" "Moderator")
    |> ignore

    context.Users.Add(createUserData memberUserId "member@example.com" "Member")
    |> ignore

    context.Spaces.Add(
        {
            Id = spaceId
            Name = "Engineering"
            ModeratorUserId = moderatorUserId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            MemberIds = Unchecked.defaultof<UserId list>
        }
    )
    |> ignore

    context.SpaceMembers.Add(
        {
            Id = Guid.NewGuid()
            UserId = memberUserId
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
        }
    )
    |> ignore

    let! _ = context.SaveChangesAsync()
    context.ChangeTracker.Clear()

    let! result = repository.GetByNameAsync("Engineering")

    Assert.True(result.IsSome)
    let space = result.Value
    Assert.False(isNull (box space.State.MemberIds))
    Assert.Single(space.State.MemberIds) |> ignore
    Assert.Equal(memberUserId, space.State.MemberIds.Head)
}

[<Fact>]
let ``AddAsync succeeds when space has null MemberIds`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection
    let repository = SpaceRepository(context, NoOpEventRepository()) :> ISpaceRepository

    let moderatorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()

    context.Users.Add(createUserData moderatorUserId "moderator2@example.com" "Moderator 2")
    |> ignore

    let malformedSpace =
        Space.fromData {
            Id = spaceId
            Name = "QA"
            ModeratorUserId = moderatorUserId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            MemberIds = Unchecked.defaultof<UserId list>
        }

    let! addResult = repository.AddAsync(malformedSpace)

    Assert.True(addResult.IsOk)
    Assert.Equal(1, context.Spaces.Count())
    Assert.Equal("QA", context.Spaces.Single().Name)
}

[<Fact>]
let ``DeleteAsync cascades delete to space scoped entities and removes OU mappings`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection
    let repository = SpaceRepository(context, NoOpEventRepository()) :> ISpaceRepository

    let moderatorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let folderId = FolderId.NewId()
    let resourceId = ResourceId.NewId()
    let appId = AppId.NewId()
    let runId = RunId.NewId()
    let now = DateTime.UtcNow

    let folderName =
        unwrapOrFail (FolderName.Create(Some "Engineering Folder")) "FolderName should be valid"

    let resourceName =
        unwrapOrFail (ResourceName.Create(Some "Engineering API")) "ResourceName should be valid"

    let resourceDescription =
        unwrapOrFail (ResourceDescription.Create(Some "Primary Engineering resource")) "Description should be valid"

    let baseUrl =
        unwrapOrFail (BaseUrl.Create(Some "https://example.com")) "BaseUrl should be valid"

    let httpMethod =
        unwrapOrFail (HttpMethod.Create("GET")) "HttpMethod should be valid"

    let runStatus =
        unwrapOrFail (RunStatus.Create("success")) "RunStatus should be valid"

    context.Users.Add(createUserData moderatorUserId "moderator-delete@example.com" "Moderator")
    |> ignore

    context.Users.Add(createUserData memberUserId "member-delete@example.com" "Member")
    |> ignore

    context.Spaces.Add(
        {
            Id = spaceId
            Name = "Delete Cascade Space"
            ModeratorUserId = moderatorUserId
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
            MemberIds = []
        }
    )
    |> ignore

    context.SpaceMembers.Add(
        {
            Id = Guid.NewGuid()
            UserId = memberUserId
            SpaceId = spaceId
            CreatedAt = now
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = folderId
            Name = folderName
            ParentId = None
            SpaceId = spaceId
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
            Children = []
        }
    )
    |> ignore

    context.Resources.Add(
        {
            Id = resourceId
            Name = resourceName
            Description = resourceDescription
            SpaceId = spaceId
            ResourceKind = ResourceKind.Http
            BaseUrl = Some baseUrl
            UrlParameters = []
            Headers = []
            Body = []
            DatabaseName = None
            DatabaseHost = None
            DatabasePort = None
            DatabaseEngine = None
            DatabaseAuthScheme = None
            DatabaseUsername = None
            DatabasePassword = None
            UseSsl = false
            EnableSshTunnel = false
            ConnectionOptions = []
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
        }
    )
    |> ignore

    context.Apps.Add(
        {
            Id = appId
            Name = "Delete Cascade App"
            FolderId = folderId
            ResourceId = resourceId
            HttpMethod = httpMethod
            Inputs = []
            UrlPath = None
            UrlParameters = []
            Headers = []
            Body = []
            UseDynamicJsonBody = false
            SqlConfig = None
            Description = None
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
        }
    )
    |> ignore

    context.Runs.Add(
        {
            Id = runId
            AppId = appId
            Status = runStatus
            InputValues = []
            ExecutableRequest = None
            ExecutedSql = None
            Response = None
            ErrorMessage = None
            StartedAt = None
            CompletedAt = None
            CreatedAt = now
            IsDeleted = false
        }
    )
    |> ignore

    context.IdentityGroupSpaceMappings.Add(
        {
            Id = Guid.NewGuid()
            GroupKey = "ou:/engineering"
            SpaceId = spaceId
            IsActive = true
            CreatedByUserId = moderatorUserId
            UpdatedByUserId = moderatorUserId
            CreatedAt = now
            UpdatedAt = now
        }
    )
    |> ignore

    let! _ = context.SaveChangesAsync()
    context.ChangeTracker.Clear()

    let spaceToDelete =
        Space.fromData {
            Id = spaceId
            Name = "Delete Cascade Space"
            ModeratorUserId = moderatorUserId
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
            MemberIds = [ memberUserId ]
        }
        |> Space.markForDeletion moderatorUserId

    let! deleteResult = repository.DeleteAsync(spaceToDelete)

    Assert.True(deleteResult.IsOk)

    let deletedSpace =
        context.Spaces.IgnoreQueryFilters().Single(fun s -> s.Id = spaceId)

    let deletedFolder =
        context.Folders.IgnoreQueryFilters().Single(fun f -> f.Id = folderId)

    let deletedResource =
        context.Resources.IgnoreQueryFilters().Single(fun r -> r.Id = resourceId)

    let deletedApp = context.Apps.IgnoreQueryFilters().Single(fun a -> a.Id = appId)

    let deletedRun = context.Runs.IgnoreQueryFilters().Single(fun r -> r.Id = runId)

    Assert.True(deletedSpace.IsDeleted)
    Assert.True(deletedFolder.IsDeleted)
    Assert.True(deletedResource.IsDeleted)
    Assert.True(deletedApp.IsDeleted)
    Assert.True(deletedRun.IsDeleted)

    Assert.Equal(0, context.SpaceMembers.Where(fun sm -> sm.SpaceId = spaceId).Count())
    Assert.Equal(0, context.IdentityGroupSpaceMappings.Where(fun m -> m.SpaceId = spaceId).Count())
}

[<Fact>]
let ``DeleteAsync succeeds after GetByIdAsync without tracking conflicts`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection
    let repository = SpaceRepository(context, NoOpEventRepository()) :> ISpaceRepository

    let moderatorUserId = UserId.NewId()
    let memberUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let now = DateTime.UtcNow

    context.Users.Add(createUserData moderatorUserId "moderator-tracking@example.com" "Moderator")
    |> ignore

    context.Users.Add(createUserData memberUserId "member-tracking@example.com" "Member")
    |> ignore

    context.Spaces.Add(
        {
            Id = spaceId
            Name = "Tracking Conflict Space"
            ModeratorUserId = moderatorUserId
            CreatedAt = now
            UpdatedAt = now
            IsDeleted = false
            MemberIds = []
        }
    )
    |> ignore

    context.SpaceMembers.Add(
        {
            Id = Guid.NewGuid()
            UserId = memberUserId
            SpaceId = spaceId
            CreatedAt = now
        }
    )
    |> ignore

    let! _ = context.SaveChangesAsync()

    let! maybeSpace = repository.GetByIdAsync(spaceId)
    Assert.True(maybeSpace.IsSome)

    let spaceWithDeleteEvent = maybeSpace.Value |> Space.markForDeletion moderatorUserId

    let! deleteResult = repository.DeleteAsync(spaceWithDeleteEvent)
    Assert.True(deleteResult.IsOk)
}