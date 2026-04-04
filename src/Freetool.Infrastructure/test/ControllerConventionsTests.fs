module Freetool.Infrastructure.Tests.ControllerConventionsTests

open System.Reflection
open Microsoft.AspNetCore.Mvc
open Xunit
open Freetool.Api.Controllers

// ASP.NET Core controller activation happens at runtime, not at F# compile time.
// A controller can compile with multiple public constructors, but MVC will throw on
// the first request if more than one constructor is resolvable from DI. Keep this
// test in the normal backend suite so signoff-pr.sh catches that class of failure.

let private controllerTypes =
    typeof<AppController>.Assembly.GetTypes()
    |> Array.filter (fun current -> not current.IsAbstract && typeof<ControllerBase>.IsAssignableFrom(current))

let private formatConstructorSignature (ctor: ConstructorInfo) =
    let parameters =
        ctor.GetParameters()
        |> Array.map (fun parameter -> parameter.ParameterType.Name)
        |> String.concat ", "

    $"{ctor.DeclaringType.FullName}({parameters})"

[<Fact>]
let ``controllers expose exactly one public constructor`` () =
    let offenders =
        controllerTypes
        |> Array.choose (fun current ->
            let constructors =
                current.GetConstructors(BindingFlags.Public ||| BindingFlags.Instance)

            if constructors.Length = 1 then
                None
            else
                let signatures =
                    constructors |> Array.map formatConstructorSignature |> String.concat "\n  "

                Some $"{current.FullName} exposes {constructors.Length} public constructors:\n  {signatures}")

    Assert.True(offenders.Length = 0, "Controller constructor violations:\n" + String.concat "\n" offenders)