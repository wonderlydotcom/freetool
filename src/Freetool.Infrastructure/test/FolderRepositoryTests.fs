module Freetool.Infrastructure.Tests.FolderRepositoryTests

open System
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

type NoOpFolderEventRepository() =
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

let private folderName value =
    FolderName.Create(Some value)
    |> Result.defaultWith (fun error -> failwith $"Invalid test folder name: {error}")

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

[<Fact>]
let ``GetByIdAsync returns folder when persisted FolderData has null Children`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection

    let repository =
        FolderRepository(context, NoOpFolderEventRepository()) :> IFolderRepository

    let moderatorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let rootFolderId = FolderId.NewId()
    let childFolderId = FolderId.NewId()

    context.Users.Add(createUserData moderatorUserId "folder-moderator@example.com" "Folder Moderator")
    |> ignore

    context.Spaces.Add(
        {
            Id = spaceId
            Name = "Folder Space"
            ModeratorUserId = moderatorUserId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            MemberIds = []
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = rootFolderId
            Name = folderName "Root"
            ParentId = None
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = Unchecked.defaultof<FolderData list>
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = childFolderId
            Name = folderName "Child"
            ParentId = Some rootFolderId
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = Unchecked.defaultof<FolderData list>
        }
    )
    |> ignore

    let! _ = context.SaveChangesAsync()
    context.ChangeTracker.Clear()

    let! result = repository.GetByIdAsync(rootFolderId)

    Assert.True(result.IsSome)
    let root = result.Value
    Assert.False(isNull (box root.State.Children))
    Assert.Single(root.State.Children) |> ignore
    Assert.Equal(childFolderId, root.State.Children.Head.Id)
}

[<Fact>]
let ``GetChildrenAsync initializes Children list for each child when persisted value is null`` () : Task = task {
    let context, connection = createSqliteContext ()
    use context = context
    use connection = connection

    let repository =
        FolderRepository(context, NoOpFolderEventRepository()) :> IFolderRepository

    let moderatorUserId = UserId.NewId()
    let spaceId = SpaceId.NewId()
    let parentFolderId = FolderId.NewId()
    let childAId = FolderId.NewId()
    let childBId = FolderId.NewId()

    context.Users.Add(createUserData moderatorUserId "folder-moderator2@example.com" "Folder Moderator 2")
    |> ignore

    context.Spaces.Add(
        {
            Id = spaceId
            Name = "Folder Space 2"
            ModeratorUserId = moderatorUserId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            MemberIds = []
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = parentFolderId
            Name = folderName "Parent"
            ParentId = None
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = Unchecked.defaultof<FolderData list>
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = childAId
            Name = folderName "Child A"
            ParentId = Some parentFolderId
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = Unchecked.defaultof<FolderData list>
        }
    )
    |> ignore

    context.Folders.Add(
        {
            Id = childBId
            Name = folderName "Child B"
            ParentId = Some parentFolderId
            SpaceId = spaceId
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            Children = Unchecked.defaultof<FolderData list>
        }
    )
    |> ignore

    let! _ = context.SaveChangesAsync()
    context.ChangeTracker.Clear()

    let! children = repository.GetChildrenAsync(parentFolderId)

    Assert.Equal(2, children.Length)

    for child in children do
        Assert.False(isNull (box child.State.Children))
        Assert.Empty(child.State.Children)
}