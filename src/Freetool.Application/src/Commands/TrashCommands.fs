namespace Freetool.Application.Commands

open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type TrashCommand =
    | GetTrashBySpace of spaceId: string * skip: int * take: int
    | RestoreApp of actorUserId: UserId * appId: string
    | RestoreFolder of actorUserId: UserId * folderId: string
    | RestoreResource of actorUserId: UserId * resourceId: string

type TrashCommandResult =
    | TrashListResult of PagedResult<TrashItemDto>
    | RestoreAppResult of RestoreResultDto
    | RestoreFolderResult of RestoreResultDto
    | RestoreResourceResult of RestoreResultDto