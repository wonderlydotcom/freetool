namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type RunCommandResult =
    | RunResult of RunData
    | RunsResult of PagedResult<RunData>
    | RunUnitResult of unit

type RunCommand =
    | CreateRun of actorUserId: UserId * appId: string * currentUser: CurrentUser * CreateRunDto
    | GetRunById of runId: string
    | GetRunsByAppId of appId: string * skip: int * take: int
    | GetRunsByStatus of status: string * skip: int * take: int
    | GetRunsByAppIdAndStatus of appId: string * status: string * skip: int * take: int