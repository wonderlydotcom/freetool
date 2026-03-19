namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type DashboardCommandResult =
    | DashboardResult of DashboardData
    | DashboardsResult of PagedResult<DashboardData>
    | DashboardPrepareResult of DashboardPrepareResponseDto
    | DashboardActionResult of DashboardActionResponseDto
    | DashboardUnitResult of unit

type DashboardCommand =
    | CreateDashboard of actorUserId: UserId * CreateDashboardDto
    | GetDashboardById of dashboardId: string
    | GetDashboardsByFolderId of folderId: string * skip: int * take: int
    | GetAllDashboards of skip: int * take: int
    | GetDashboardsBySpaceIds of spaceIds: SpaceId list * skip: int * take: int
    | UpdateDashboardName of actorUserId: UserId * dashboardId: string * UpdateDashboardNameDto
    | UpdateDashboardConfiguration of actorUserId: UserId * dashboardId: string * UpdateDashboardConfigurationDto
    | UpdateDashboardPrepareApp of actorUserId: UserId * dashboardId: string * UpdateDashboardPrepareAppDto
    | PrepareDashboard of actorUserId: UserId * dashboardId: string * currentUser: CurrentUser * PrepareDashboardDto
    | RunDashboardAction of
        actorUserId: UserId *
        dashboardId: string *
        actionId: ActionId *
        currentUser: CurrentUser *
        RunDashboardActionDto
    | DeleteDashboard of actorUserId: UserId * dashboardId: string