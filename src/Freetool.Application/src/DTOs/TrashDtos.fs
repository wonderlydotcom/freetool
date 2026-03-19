namespace Freetool.Application.DTOs

open System

type TrashItemDto = {
    Id: string
    Name: string
    ItemType: string // "app" | "folder" | "resource"
    SpaceId: string
    DeletedAt: DateTime
}

type TrashListDto = {
    Items: TrashItemDto list
    TotalCount: int
}

type RestoreResultDto = {
    RestoredId: string
    NewName: string option
    AutoRestoredResourceId: string option
}