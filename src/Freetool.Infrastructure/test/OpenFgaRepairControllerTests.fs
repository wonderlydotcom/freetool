module Freetool.Infrastructure.Tests.OpenFgaRepairControllerTests

open System
open System.Threading.Tasks
open Xunit
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging.Abstractions
open Freetool.Api.Controllers
open Freetool.Api.Services
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects

type MockAuthorizationService(checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool) =
    interface IAuthorizationService with
        member _.CreateStoreAsync req =
            Task.FromResult({ Id = "store-1"; Name = req.Name })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())
        member _.CreateRelationshipsAsync _ = Task.FromResult(())
        member _.UpdateRelationshipsAsync _ = Task.FromResult(())
        member _.DeleteRelationshipsAsync _ = Task.FromResult(())
        member _.StoreExistsAsync _ = Task.FromResult(true)

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
            Task.FromResult(checkPermissionFn subject relation obj)

        member _.BatchCheckPermissionsAsync (subject: AuthSubject) (relations: AuthRelation list) (obj: AuthObject) =
            let results =
                relations
                |> List.map (fun relation -> (relation, checkPermissionFn subject relation obj))
                |> Map.ofList

            Task.FromResult(results)

type CapturingRepairService(summary: OpenFgaSpaceAuthorizationRepairSummary) =
    let mutable capturedArgs: (bool * string option) option = None

    member _.CapturedArgs = capturedArgs

    interface IOpenFgaSpaceAuthorizationRepairService with
        member _.RepairAsync apply requestedSpaceId =
            capturedArgs <- Some(apply, requestedSpaceId)
            Task.FromResult(summary)

let createSummary apply spaceFilter = {
    Apply = apply
    SpaceFilter = spaceFilter
    SpacesExamined = 1
    SpacesWithDrift = 1
    Results = [
        {
            SpaceId = Guid.NewGuid().ToString()
            SpaceName = "Engineering"
            DesiredRelationships = [ "space:engineering#run_app@space:engineering#member" ]
            CurrentRelationships = []
            RelationshipsToAdd = [ "space:engineering#run_app@space:engineering#member" ]
            RelationshipsToRemove = []
            Applied = apply
            Warnings = []
        }
    ]
}

let createController
    (checkPermissionFn: AuthSubject -> AuthRelation -> AuthObject -> bool)
    (summary: OpenFgaSpaceAuthorizationRepairSummary)
    =
    let authService =
        MockAuthorizationService(checkPermissionFn) :> IAuthorizationService

    let repairService = CapturingRepairService(summary)

    let controller =
        OpenFgaRepairController(
            repairService :> IOpenFgaSpaceAuthorizationRepairService,
            authService,
            NullLogger<OpenFgaRepairController>.Instance
        )

    let httpContext = DefaultHttpContext()
    httpContext.Items.["UserId"] <- UserId.NewId()
    controller.ControllerContext <- ControllerContext(HttpContext = httpContext)

    (controller, repairService)

[<Fact>]
let ``RepairDefaultMemberPermissions returns 403 for non org admin`` () : Task = task {
    let controller, repairService =
        createController (fun _ _ _ -> false) (createSummary true None)

    let! result = controller.RepairDefaultMemberPermissions(true, null)

    match result with
    | :? ObjectResult as objectResult -> Assert.Equal(403, objectResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected ObjectResult")

    Assert.True(repairService.CapturedArgs.IsNone)
}

[<Fact>]
let ``RepairDefaultMemberPermissions returns 400 for invalid space id`` () : Task = task {
    let controller, repairService =
        createController
            (fun subject relation obj ->
                match subject, relation, obj with
                | User _, OrganizationAdmin, OrganizationObject "default" -> true
                | _ -> false)
            (createSummary true None)

    let! result = controller.RepairDefaultMemberPermissions(true, "not-a-guid")

    match result with
    | :? BadRequestObjectResult -> Assert.True(true)
    | :? ObjectResult as objectResult -> Assert.Equal(400, objectResult.StatusCode.Value)
    | _ -> Assert.True(false, "Expected bad request")

    Assert.True(repairService.CapturedArgs.IsNone)
}

[<Fact>]
let ``RepairDefaultMemberPermissions returns 200 and forwards arguments for org admin`` () : Task = task {
    let targetSpaceId = Guid.NewGuid().ToString()
    let expectedSummary = createSummary true (Some targetSpaceId)

    let controller, repairService =
        createController
            (fun subject relation obj ->
                match subject, relation, obj with
                | User _, OrganizationAdmin, OrganizationObject "default" -> true
                | _ -> false)
            expectedSummary

    let! result = controller.RepairDefaultMemberPermissions(true, targetSpaceId)

    match result with
    | :? OkObjectResult as okResult ->
        let payload = Assert.IsType<OpenFgaSpaceAuthorizationRepairSummary>(okResult.Value)

        Assert.Equal(expectedSummary.SpacesExamined, payload.SpacesExamined)
        Assert.Equal(expectedSummary.SpaceFilter, payload.SpaceFilter)
    | _ -> Assert.True(false, "Expected OkObjectResult")

    Assert.Equal(Some(true, Some targetSpaceId), repairService.CapturedArgs)
}