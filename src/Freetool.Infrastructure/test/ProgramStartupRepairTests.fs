module Freetool.Infrastructure.Tests.ProgramStartupRepairTests

open System
open Xunit
open Microsoft.Extensions.Logging
open Freetool.Api.Services

type CapturingLogger() =
    let mutable infos: string list = []
    let mutable warnings: string list = []

    member _.Infos = infos
    member _.Warnings = warnings

    interface ILogger with
        member _.BeginScope<'State>(_state: 'State) = null
        member _.IsEnabled(_logLevel: LogLevel) = true

        member _.Log<'State>
            (logLevel: LogLevel, _eventId: EventId, state: 'State, exn: exn, formatter: Func<'State, exn, string>)
            =
            let message = formatter.Invoke(state, exn)

            match logLevel with
            | LogLevel.Warning -> warnings <- message :: warnings
            | LogLevel.Information -> infos <- message :: infos
            | _ -> ()

[<Fact>]
let ``runOpenFgaSpaceAuthorizationRepair logs success and warning summary`` () =
    let logger = CapturingLogger()

    let summary = {
        Apply = true
        SpaceFilter = None
        SpacesExamined = 2
        SpacesWithDrift = 1
        Results = [
            {
                SpaceId = Guid.NewGuid().ToString()
                SpaceName = "Engineering"
                DesiredRelationships = [ "space:engineering#run_app@space:engineering#member" ]
                CurrentRelationships = []
                RelationshipsToAdd = [ "space:engineering#run_app@space:engineering#member" ]
                RelationshipsToRemove = []
                Applied = true
                Warnings = [ "ignored unknown permission" ]
            }
        ]
    }

    OpenFgaSpaceAuthorizationRepairStartup.runOpenFgaSpaceAuthorizationRepair (logger :> ILogger) (fun () -> summary)

    Assert.Contains(logger.Infos, fun message -> message.Contains("Repairing OpenFGA space authorization"))
    Assert.Contains(logger.Infos, fun message -> message.Contains("examined 2 spaces"))
    Assert.Contains(logger.Warnings, fun message -> message.Contains("completed with 1 warnings"))

[<Fact>]
let ``runOpenFgaSpaceAuthorizationRepair logs success without warnings when none are present`` () =
    let logger = CapturingLogger()

    let summary = {
        Apply = true
        SpaceFilter = None
        SpacesExamined = 1
        SpacesWithDrift = 0
        Results = []
    }

    OpenFgaSpaceAuthorizationRepairStartup.runOpenFgaSpaceAuthorizationRepair (logger :> ILogger) (fun () -> summary)

    Assert.Contains(logger.Infos, fun message -> message.Contains("examined 1 spaces"))
    Assert.Empty(logger.Warnings)

[<Fact>]
let ``runOpenFgaSpaceAuthorizationRepair swallows repair exceptions and logs warning`` () =
    let logger = CapturingLogger()

    OpenFgaSpaceAuthorizationRepairStartup.runOpenFgaSpaceAuthorizationRepair (logger :> ILogger) (fun () ->
        raise (InvalidOperationException "boom"))

    Assert.Contains(
        logger.Warnings,
        fun message -> message.Contains("Could not repair OpenFGA space authorization tuples")
    )