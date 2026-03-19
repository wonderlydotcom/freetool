namespace Freetool.Api.Tracing

open System
open System.Diagnostics
open Microsoft.FSharp.Reflection
open Freetool.Application.Interfaces

module AutoTracing =

    let getSpanName (entityName: string) (command: obj) : string =
        let commandType = command.GetType()

        if FSharpType.IsUnion commandType then
            let case, _ = FSharpValue.GetUnionFields(command, commandType)
            let caseName = case.Name

            // Convert PascalCase to snake_case and generate span name
            let operationName =
                caseName
                |> Seq.fold
                    (fun acc c ->
                        if Char.IsUpper(c) && (acc: string).Length > 0 then
                            acc + "_" + string (Char.ToLower(c))
                        else
                            acc + string (Char.ToLower(c)))
                    ""

            $"{entityName.ToLowerInvariant()}.{operationName}"
        else
            $"{entityName.ToLowerInvariant()}.unknown"

    let getOperationType (command: obj) : string =
        let commandType = command.GetType()

        if FSharpType.IsUnion commandType then
            let case, _ = FSharpValue.GetUnionFields(command, commandType)
            let caseName = case.Name.ToLowerInvariant()

            if caseName.StartsWith("create") then
                "create"
            elif
                caseName.StartsWith("get")
                || caseName.StartsWith("find")
                || caseName.StartsWith("list")
            then
                "read"
            elif
                caseName.StartsWith("update")
                || caseName.StartsWith("set")
                || caseName.StartsWith("move")
                || caseName.Contains("update")
            then
                "update"
            elif caseName.StartsWith("delete") || caseName.StartsWith("remove") then
                "delete"
            else
                "unknown"
        else
            "unknown"

    let getAttributeName (prefix: string) (fieldName: string) : string =
        let snakeCaseName =
            fieldName
            |> Seq.fold
                (fun acc c ->
                    if Char.IsUpper(c) && (acc: string).Length > 0 then
                        acc + "_" + string (Char.ToLower(c))
                    else
                        acc + string (Char.ToLower(c)))
                ""

        $"{prefix}.{snakeCaseName}"

    let shouldSkipField (fieldName: string) : bool =
        let sensitivePatterns = [| "password"; "token"; "secret"; "key"; "credential" |]
        let lowerName = fieldName.ToLowerInvariant()
        sensitivePatterns |> Array.exists lowerName.Contains

    let rec addObjectAttributes (activity: Activity option) (prefix: string) (obj: obj) =
        if obj <> null then
            let objType = obj.GetType()

            // Handle F# record types (DTOs)
            if FSharpType.IsRecord objType then
                let fields = FSharpType.GetRecordFields objType
                let values = FSharpValue.GetRecordFields obj

                Array.zip fields values
                |> Array.iter (fun (field, value) ->
                    if not (shouldSkipField field.Name) && value <> null then
                        let attributeName = getAttributeName prefix field.Name

                        match value with
                        | :? string as s when not (String.IsNullOrEmpty(s)) ->
                            Tracing.addAttribute activity attributeName s
                        | :? int as i -> Tracing.addIntAttribute activity attributeName i
                        | :? bool as b -> Tracing.addAttribute activity attributeName (b.ToString().ToLowerInvariant())
                        | :? DateTime as dt ->
                            Tracing.addAttribute activity attributeName (dt.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                        | :? Guid as g -> Tracing.addAttribute activity attributeName (g.ToString())
                        | _ when FSharpType.IsRecord(value.GetType()) ->
                            // Recursively handle nested records (but limit depth)
                            addObjectAttributes activity attributeName value
                        | _ ->
                            let stringValue = value.ToString()

                            if not (String.IsNullOrEmpty(stringValue)) then
                                Tracing.addAttribute activity attributeName stringValue)

            // Handle discriminated unions (commands)
            elif FSharpType.IsUnion objType then
                let case, fields = FSharpValue.GetUnionFields(obj, objType)

                // Add operation type automatically
                let operationType = getOperationType obj
                Tracing.addAttribute activity "operation.type" operationType

                // Handle command parameters
                let caseFields = case.GetFields()

                Array.zip caseFields fields
                |> Array.iteri (fun i (field, value) ->
                    if not (shouldSkipField field.Name) && value <> null then
                        match value with
                        | :? string as s when not (String.IsNullOrEmpty(s)) ->
                            let attributeName =
                                if field.Name.Contains("Id") then
                                    $"{prefix}.id"
                                else
                                    getAttributeName prefix field.Name

                            Tracing.addAttribute activity attributeName s
                        | :? int as i when field.Name.ToLowerInvariant().Contains("skip") ->
                            Tracing.addAttribute activity "pagination.skip" (i.ToString())
                        | :? int as i when field.Name.ToLowerInvariant().Contains("take") ->
                            Tracing.addAttribute activity "pagination.take" (i.ToString())
                        | :? int as i -> Tracing.addIntAttribute activity (getAttributeName prefix field.Name) i
                        | _ when FSharpType.IsRecord(value.GetType()) ->
                            // Handle DTO parameters in commands
                            addObjectAttributes activity prefix value
                        | _ ->
                            let stringValue = value.ToString()

                            if not (String.IsNullOrEmpty(stringValue)) then
                                Tracing.addAttribute activity (getAttributeName prefix field.Name) stringValue)

    let addResultAttributes (activity: Activity option) (result: obj) =
        if result <> null then
            let resultType = result.GetType()

            if FSharpType.IsUnion resultType then
                let case, fields = FSharpValue.GetUnionFields(result, resultType)

                match case.Name, fields with
                | name, [| actualResult |] when name.EndsWith("Result") && not (name.EndsWith("sResult")) ->
                    // Single result (e.g., UserResult containing UserData entity)
                    addObjectAttributes activity "result" actualResult
                | name, [| pagedResult |] when name.EndsWith("sResult") ->
                    // Paged result (e.g., UsersResult containing PagedResult<UserData>)
                    if FSharpType.IsRecord(pagedResult.GetType()) then
                        let fields = FSharpType.GetRecordFields(pagedResult.GetType())
                        let values = FSharpValue.GetRecordFields(pagedResult)

                        Array.zip fields values
                        |> Array.iter (fun (field, value) ->
                            match field.Name.ToLowerInvariant(), value with
                            | "items", (:? System.Collections.ICollection as collection) ->
                                Tracing.addIntAttribute activity "result.count" collection.Count
                                // Add attributes from the first item if available and it's a record
                                let items = collection :?> System.Collections.IEnumerable
                                let firstItem = items |> Seq.cast<obj> |> Seq.tryHead

                                match firstItem with
                                | Some item when FSharpType.IsRecord(item.GetType()) ->
                                    addObjectAttributes activity "result.sample" item
                                | _ -> ()
                            | "totalcount", (:? int as total) ->
                                Tracing.addIntAttribute activity "result.total_count" total
                            | "skip", (:? int as skip) -> Tracing.addIntAttribute activity "result.skip" skip
                            | "take", (:? int as take) -> Tracing.addIntAttribute activity "result.take" take
                            | _ -> ())
                | _ -> ()

    /// Creates a tracing decorator for any command handler.
    /// Automatically extracts span names from command types and adds attributes via reflection.
    let createTracingDecorator<'TCommand, 'TResult>
        (entityName: string)
        (inner: ICommandHandler<'TCommand, 'TResult>)
        (activitySource: ActivitySource)
        =

        { new ICommandHandler<'TCommand, 'TResult> with
            member this.HandleCommand command =
                let spanName = getSpanName entityName (box command)

                Tracing.withSpan activitySource spanName (fun activity ->
                    // Add command attributes automatically using reflection
                    addObjectAttributes activity entityName (box command)

                    task {
                        let! result = inner.HandleCommand command

                        match result with
                        | Ok commandResult ->
                            // Add result attributes automatically using reflection
                            addResultAttributes activity (box commandResult)
                            Tracing.setSpanStatus activity true None
                            return result
                        | Error error ->
                            Tracing.addDomainErrorEvent activity error
                            Tracing.setSpanStatus activity false None
                            return result
                    })
        }