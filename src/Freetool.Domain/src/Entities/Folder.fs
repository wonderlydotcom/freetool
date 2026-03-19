namespace Freetool.Domain.Entities

open System
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Events

(*
 * TODO: Change the data model to be path-based parent ID's
 * This obviates the need for recursion when doing deletions (see FolderRepository)
 * It also makes the reads much faster and simpler to implement.
 *
 * It DOES slow down the writes but in my experience this type of structure is 10-1
 * read favored anyway, so slowing down writes shouldn't matter.
 *)
[<Table("Folders")>]
[<Index([| "Name"; "ParentId" |], IsUnique = true, Name = "IX_Folders_Name_ParentId")>]
// CLIMutable for EntityFramework
[<CLIMutable>]
type FolderData = {
    [<Key>]
    Id: FolderId

    [<Required>]
    [<MaxLength(100)>]
    Name: FolderName

    ParentId: FolderId option

    [<Required>]
    SpaceId: SpaceId

    [<Required>]
    [<JsonIgnore>]
    CreatedAt: DateTime

    [<Required>]
    [<JsonIgnore>]
    UpdatedAt: DateTime

    [<JsonIgnore>]
    IsDeleted: bool

    [<NotMapped>]
    [<JsonPropertyName("children")>]
    mutable Children: FolderData list
}

type Folder = EventSourcingAggregate<FolderData>

module FolderAggregateHelpers =
    let getEntityId (folder: Folder) : FolderId = folder.State.Id

    let implementsIEntity (folder: Folder) =
        { new IEntity<FolderId> with
            member _.Id = folder.State.Id
        }

type UnvalidatedFolder = Folder // From DTOs - potentially unsafe
type ValidatedFolder = Folder // Validated domain model and database data

module Folder =
    let fromData (folderData: FolderData) : ValidatedFolder = {
        State = folderData
        UncommittedEvents = []
    }

    let create
        (actorUserId: UserId)
        (name: string)
        (parentId: FolderId option)
        (spaceId: SpaceId)
        : Result<ValidatedFolder, DomainError> =
        match FolderName.Create(Some name) with
        | Error err -> Error err
        | Ok validName ->
            let folderData = {
                Id = FolderId.NewId()
                Name = validName
                ParentId = parentId
                SpaceId = spaceId
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                IsDeleted = false
                Children = []
            }

            let folderCreatedEvent =
                FolderEvents.folderCreated actorUserId folderData.Id validName parentId spaceId

            Ok {
                State = folderData
                UncommittedEvents = [ folderCreatedEvent :> IDomainEvent ]
            }

    let updateName
        (actorUserId: UserId)
        (newName: string)
        (folder: ValidatedFolder)
        : Result<ValidatedFolder, DomainError> =
        match FolderName.Create(Some newName) with
        | Error err -> Error err
        | Ok validName ->
            let oldName = folder.State.Name

            let updatedFolderData = {
                folder.State with
                    Name = validName
                    UpdatedAt = DateTime.UtcNow
            }

            let nameChangedEvent =
                FolderEvents.folderUpdated actorUserId folder.State.Id [ FolderChange.NameChanged(oldName, validName) ]

            Ok {
                State = updatedFolderData
                UncommittedEvents = folder.UncommittedEvents @ [ nameChangedEvent :> IDomainEvent ]
            }

    let moveToParent (actorUserId: UserId) (newParentId: FolderId option) (folder: ValidatedFolder) : ValidatedFolder =
        let oldParentId = folder.State.ParentId

        let updatedFolderData = {
            folder.State with
                ParentId = newParentId
                UpdatedAt = DateTime.UtcNow
        }

        let parentChangedEvent =
            FolderEvents.folderUpdated actorUserId folder.State.Id [
                FolderChange.ParentChanged(oldParentId, newParentId)
            ]

        {
            State = updatedFolderData
            UncommittedEvents = folder.UncommittedEvents @ [ parentChangedEvent :> IDomainEvent ]
        }

    let markForDeletion (actorUserId: UserId) (folder: ValidatedFolder) : ValidatedFolder =
        let folderDeletedEvent =
            FolderEvents.folderDeleted actorUserId folder.State.Id folder.State.Name

        {
            folder with
                UncommittedEvents = folder.UncommittedEvents @ [ folderDeletedEvent :> IDomainEvent ]
        }

    let restore (actorUserId: UserId) (newName: FolderName option) (folder: ValidatedFolder) : ValidatedFolder =
        let finalName = newName |> Option.defaultValue folder.State.Name

        let folderRestoredEvent =
            FolderEvents.folderRestored actorUserId folder.State.Id finalName

        {
            folder with
                State = {
                    folder.State with
                        Name = finalName
                        IsDeleted = false
                        UpdatedAt = DateTime.UtcNow
                }
                UncommittedEvents = folder.UncommittedEvents @ [ folderRestoredEvent :> IDomainEvent ]
        }

    let getUncommittedEvents (folder: ValidatedFolder) : IDomainEvent list = folder.UncommittedEvents

    let markEventsAsCommitted (folder: ValidatedFolder) : ValidatedFolder = { folder with UncommittedEvents = [] }

    let getId (folder: Folder) : FolderId = folder.State.Id

    let getName (folder: Folder) : string = folder.State.Name.Value

    let getParentId (folder: Folder) : FolderId option = folder.State.ParentId

    let getSpaceId (folder: Folder) : SpaceId = folder.State.SpaceId

    let getCreatedAt (folder: Folder) : DateTime = folder.State.CreatedAt

    let getUpdatedAt (folder: Folder) : DateTime = folder.State.UpdatedAt

    let isRoot (folder: Folder) : bool = getParentId folder |> Option.isNone