namespace Freetool.Application.Handlers

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Application.Interfaces
open Freetool.Application.Commands
open Freetool.Application.DTOs

module DashboardHandler =
    type private DashboardActionDefinition = { Id: ActionId; AppId: AppId }

    type private BindingSource =
        | LoadInput of string
        | ActionInput of string
        | PrepareOutput of string
        | Literal of string

    type private BindingDefinition = {
        AppId: AppId option
        ActionId: ActionId option
        InputName: string
        Source: BindingSource
    }

    let private parseDashboardId (dashboardId: string) : Result<DashboardId, DomainError> =
        match Guid.TryParse dashboardId with
        | true, guid -> Ok(DashboardId.FromGuid guid)
        | false, _ -> Error(ValidationError "Invalid dashboard ID format")

    let private parseFolderId (folderId: string) : Result<FolderId, DomainError> =
        match Guid.TryParse folderId with
        | true, guid -> Ok(FolderId.FromGuid guid)
        | false, _ -> Error(ValidationError "Invalid folder ID format")

    let private parseAppId (appId: string) : Result<AppId, DomainError> =
        match Guid.TryParse appId with
        | true, guid -> Ok(AppId.FromGuid guid)
        | false, _ -> Error(ValidationError "Invalid app ID format")

    let private parsePrepareAppId (prepareAppId: string option) : Result<AppId option, DomainError> =
        match prepareAppId with
        | None -> Ok None
        | Some value when String.IsNullOrWhiteSpace(value) -> Ok None
        | Some value -> parseAppId value |> Result.map Some

    let private parseRunId (runId: string) : Result<RunId, DomainError> =
        match Guid.TryParse runId with
        | true, guid -> Ok(RunId.FromGuid guid)
        | false, _ -> Error(ValidationError "Invalid run ID format")

    let private validatePagination (skip: int) (take: int) : Result<unit, DomainError> =
        if skip < 0 then
            Error(ValidationError "Skip cannot be negative")
        elif take <= 0 || take > 100 then
            Error(ValidationError "Take must be between 1 and 100")
        else
            Ok()

    let private getStringProperty (propertyName: string) (node: JsonNode) : string option =
        match node with
        | :? JsonObject as jsonObject ->
            match jsonObject[propertyName] with
            | null -> None
            | value ->
                match value with
                | :? JsonValue as jsonValue ->
                    let mutable stringValue = ""

                    if jsonValue.TryGetValue(&stringValue) then
                        Some stringValue
                    else
                        Some(value.ToJsonString().Trim('"'))
                | _ -> Some(value.ToJsonString().Trim('"'))
        | _ -> None

    let private tryGetStringProperty (propertyNames: string list) (node: JsonNode) : string option =
        propertyNames
        |> List.tryPick (fun propertyName ->
            getStringProperty propertyName node
            |> Option.bind (fun value ->
                if String.IsNullOrWhiteSpace(value) then
                    None
                else
                    Some value))

    let private tryGetStringFromObject (propertyName: string) (jsonObject: JsonObject) : string option =
        match jsonObject[propertyName] with
        | null -> None
        | value -> getStringProperty propertyName jsonObject

    let private parseActionDefinition (actionNode: JsonNode) : Result<DashboardActionDefinition, DomainError> =
        let actionIdString = tryGetStringProperty [ "id"; "actionId" ] actionNode

        let appIdValue =
            tryGetStringProperty [ "appId" ] actionNode |> Option.defaultValue ""

        if String.IsNullOrWhiteSpace(appIdValue) then
            Error(ValidationError "Dashboard action is missing appId")
        else
            match ActionId.Create(actionIdString), parseAppId appIdValue with
            | Error err, _ -> Error err
            | _, Error err -> Error err
            | Ok actionId, Ok appId -> Ok { Id = actionId; AppId = appId }

    let private parseBindingSource (bindingNode: JsonNode) : Result<BindingSource, DomainError> =
        let inline invalidBinding () =
            Error(ValidationError "Invalid dashboard binding source")

        let sourceType =
            tryGetStringProperty [ "sourceType"; "bindingType" ] bindingNode
            |> Option.orElseWith (fun () ->
                match bindingNode with
                | :? JsonObject as bindingObject ->
                    match bindingObject["source"] with
                    | :? JsonObject as sourceObject -> tryGetStringFromObject "type" sourceObject
                    | _ -> None
                | _ -> None)
            |> Option.map (fun value -> value.Trim().ToLowerInvariant())

        let sourceKey =
            tryGetStringProperty [ "sourceKey"; "key"; "sourceInput" ] bindingNode
            |> Option.orElseWith (fun () ->
                match bindingNode with
                | :? JsonObject as bindingObject ->
                    match bindingObject["source"] with
                    | :? JsonObject as sourceObject ->
                        [ "key"; "input"; "path" ]
                        |> List.tryPick (fun key -> tryGetStringFromObject key sourceObject)
                    | _ -> None
                | _ -> None)

        let literalValue =
            tryGetStringProperty [ "literalValue"; "value" ] bindingNode
            |> Option.orElseWith (fun () ->
                match bindingNode with
                | :? JsonObject as bindingObject ->
                    match bindingObject["source"] with
                    | :? JsonObject as sourceObject -> tryGetStringFromObject "value" sourceObject
                    | _ -> None
                | _ -> None)

        match sourceType with
        | Some "load_input" ->
            match sourceKey with
            | Some key -> Ok(LoadInput key)
            | None -> invalidBinding ()
        | Some "action_input" ->
            match sourceKey with
            | Some key -> Ok(ActionInput key)
            | None -> invalidBinding ()
        | Some "prepare_output" ->
            match sourceKey with
            | Some key -> Ok(PrepareOutput key)
            | None -> invalidBinding ()
        | Some "literal" ->
            match literalValue with
            | Some value -> Ok(Literal value)
            | None -> invalidBinding ()
        | Some "previous_action_output" ->
            Error(ValidationError "previous_action_output bindings are not supported in dashboard runtime v1")
        | _ -> invalidBinding ()

    let private parseBindingDefinition (bindingNode: JsonNode) : Result<BindingDefinition, DomainError> =
        let actionIdResult =
            match tryGetStringProperty [ "actionId" ] bindingNode with
            | None -> Ok None
            | Some actionId -> ActionId.Create(Some actionId) |> Result.map Some

        let appIdResult =
            match tryGetStringProperty [ "appId" ] bindingNode with
            | None -> Ok None
            | Some appId -> parseAppId appId |> Result.map Some

        let inputName =
            tryGetStringProperty [ "inputName"; "appInputName"; "targetInput" ] bindingNode
            |> Option.defaultValue ""

        if String.IsNullOrWhiteSpace(inputName) then
            Error(ValidationError "Dashboard binding inputName is required")
        else
            match actionIdResult, appIdResult, parseBindingSource bindingNode with
            | Error err, _, _ -> Error err
            | _, Error err, _ -> Error err
            | _, _, Error err -> Error err
            | Ok actionId, Ok appId, Ok source ->
                Ok {
                    AppId = appId
                    ActionId = actionId
                    InputName = inputName
                    Source = source
                }

    let private parseDashboardConfiguration
        (configuration: string)
        : Result<Map<ActionId, DashboardActionDefinition> * BindingDefinition list, DomainError> =
        try
            let root = JsonNode.Parse(configuration)

            if isNull root then
                Error(ValidationError "Dashboard configuration is invalid JSON")
            else
                let actions =
                    match root["actions"] with
                    | :? JsonArray as actionsArray ->
                        actionsArray
                        |> Seq.filter (fun actionNode -> not (isNull actionNode))
                        |> Seq.toList
                    | _ -> []

                let bindings =
                    match root["bindings"] with
                    | :? JsonArray as bindingsArray ->
                        bindingsArray
                        |> Seq.filter (fun bindingNode -> not (isNull bindingNode))
                        |> Seq.toList
                    | _ -> []

                let parsedActions =
                    let rec collect
                        (state: Map<ActionId, DashboardActionDefinition>)
                        (remainingActions: JsonNode list)
                        : Result<Map<ActionId, DashboardActionDefinition>, DomainError> =
                        match remainingActions with
                        | [] -> Ok state
                        | actionNode :: rest ->
                            match parseActionDefinition actionNode with
                            | Error err -> Error err
                            | Ok action ->
                                if state.ContainsKey(action.Id) then
                                    Error(ValidationError $"Duplicate dashboard action id '{action.Id.Value}'")
                                else
                                    collect (state.Add(action.Id, action)) rest

                    collect Map.empty actions

                let parsedBindings =
                    bindings
                    |> List.map parseBindingDefinition
                    |> List.fold
                        (fun acc bindingResult ->
                            match acc, bindingResult with
                            | Error err, _ -> Error err
                            | _, Error err -> Error err
                            | Ok state, Ok binding -> Ok(binding :: state))
                        (Ok [])
                    |> Result.map List.rev

                match parsedActions, parsedBindings with
                | Error err, _ -> Error err
                | _, Error err -> Error err
                | Ok actionMap, Ok bindingList -> Ok(actionMap, bindingList)
        with ex ->
            Error(ValidationError $"Dashboard configuration is invalid JSON: {ex.Message}")

    let private tryResolveJsonPath (jsonText: string) (path: string) : string option =
        try
            let root = JsonNode.Parse(jsonText)

            if isNull root then
                None
            else
                let segments =
                    path.Split('.', StringSplitOptions.RemoveEmptyEntries) |> Array.toList

                let rec walk (currentNode: JsonNode option) (remainingSegments: string list) : JsonNode option =
                    match currentNode, remainingSegments with
                    | None, _ -> None
                    | Some node, [] -> Some node
                    | Some node, segment :: rest ->
                        match node with
                        | :? JsonObject as jsonObject ->
                            match jsonObject[segment] with
                            | null -> None
                            | value -> walk (Some value) rest
                        | :? JsonArray as jsonArray ->
                            match Int32.TryParse segment with
                            | true, index when index >= 0 && index < jsonArray.Count ->
                                walk (Some jsonArray[index]) rest
                            | _ -> None
                        | _ -> None

                match walk (Some root) segments with
                | None -> None
                | Some node ->
                    match node with
                    | :? JsonValue as jsonValue ->
                        let mutable stringValue = ""

                        if jsonValue.TryGetValue(&stringValue) then
                            Some stringValue
                        else
                            Some(node.ToJsonString())
                    | _ -> Some(node.ToJsonString())
        with _ ->
            None

    let private buildInputLookup (inputs: RunInputDto list) : Map<string, string> =
        inputs
        |> List.fold
            (fun state input ->
                if String.IsNullOrWhiteSpace(input.Title) then
                    state
                else
                    state.Add(input.Title, input.Value))
            Map.empty

    let private bindingsForTarget
        (appId: AppId)
        (actionId: ActionId option)
        (bindings: BindingDefinition list)
        : BindingDefinition list =
        bindings
        |> List.filter (fun binding ->
            let appMatch =
                match binding.AppId with
                | Some bindingAppId -> bindingAppId = appId
                | None -> false

            let actionMatch =
                match actionId with
                | Some targetActionId ->
                    match binding.ActionId with
                    | Some bindingActionId -> bindingActionId = targetActionId
                    | None -> false
                | None -> false

            appMatch || actionMatch)

    let private resolveBindingValue
        (binding: BindingDefinition)
        (loadInputs: Map<string, string>)
        (actionInputs: Map<string, string>)
        (prepareResponse: string option)
        : Result<string, DomainError> =
        match binding.Source with
        | LoadInput key ->
            match loadInputs.TryFind key with
            | Some value -> Ok value
            | None -> Error(ValidationError $"Missing load input '{key}' for binding '{binding.InputName}'")
        | ActionInput key ->
            match actionInputs.TryFind key with
            | Some value -> Ok value
            | None -> Error(ValidationError $"Missing action input '{key}' for binding '{binding.InputName}'")
        | PrepareOutput path ->
            match prepareResponse with
            | None -> Error(ValidationError $"Binding '{binding.InputName}' requires prepare output '{path}'")
            | Some response ->
                match tryResolveJsonPath response path with
                | Some value -> Ok value
                | None ->
                    Error(
                        ValidationError
                            $"Could not resolve prepare output path '{path}' for binding '{binding.InputName}'"
                    )
        | Literal value -> Ok value

    let private buildRunInputs
        (targetAppId: AppId)
        (targetActionId: ActionId option)
        (loadInputDtos: RunInputDto list)
        (actionInputDtos: RunInputDto list)
        (prepareResponse: string option)
        (bindings: BindingDefinition list)
        : Result<RunInputDto list, DomainError> =
        let targetBindings = bindingsForTarget targetAppId targetActionId bindings

        if List.isEmpty targetBindings then
            let combinedInputs =
                loadInputDtos @ actionInputDtos
                |> List.groupBy (fun input -> input.Title)
                |> List.map (fun (_, grouped) -> grouped |> List.last)

            Ok combinedInputs
        else
            let loadLookup = buildInputLookup loadInputDtos
            let actionLookup = buildInputLookup actionInputDtos

            targetBindings
            |> List.fold
                (fun acc binding ->
                    match acc with
                    | Error err -> Error err
                    | Ok runInputs ->
                        match resolveBindingValue binding loadLookup actionLookup prepareResponse with
                        | Error err -> Error err
                        | Ok resolvedValue ->
                            Ok(
                                {
                                    Title = binding.InputName
                                    Value = resolvedValue
                                }
                                :: runInputs
                            ))
                (Ok [])
            |> Result.map List.rev

    let private createRunDto (inputs: RunInputDto list) : CreateRunDto = {
        InputValues = inputs
        DynamicBody = None
    }

    let private saveRuntimeEventAsync
        (eventRepository: IEventRepository)
        (eventData: IDomainEvent)
        : Task<Result<unit, DomainError>> =
        task {
            try
                do! eventRepository.SaveEventAsync eventData
                do! eventRepository.CommitAsync()
                return Ok()
            with ex ->
                return Error(InvalidOperation $"Failed to save dashboard runtime event: {ex.Message}")
        }

    let private executeRunAsync
        (runRepository: IRunRepository)
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (sqlExecutionService: ISqlExecutionService)
        (actorUserId: UserId)
        (appId: AppId)
        (currentUser: CurrentUser)
        (runInputs: RunInputDto list)
        : Task<Result<RunData, DomainError>> =
        task {
            let runDto = createRunDto runInputs

            let! runResult =
                RunHandler.handleCommand
                    runRepository
                    appRepository
                    resourceRepository
                    sqlExecutionService
                    (CreateRun(actorUserId, appId.Value.ToString(), currentUser, runDto))

            match runResult with
            | Error err -> return Error err
            | Ok(RunResult runData) -> return Ok runData
            | Ok _ -> return Error(InvalidOperation "Unexpected run result type")
        }

    let private handlePrepareDashboard
        (dashboardRepository: IDashboardRepository)
        (runRepository: IRunRepository)
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (sqlExecutionService: ISqlExecutionService)
        (eventRepository: IEventRepository)
        (actorUserId: UserId)
        (dashboardId: string)
        (currentUser: CurrentUser)
        (dto: PrepareDashboardDto)
        : Task<Result<DashboardCommandResult, DomainError>> =
        task {
            match parseDashboardId dashboardId with
            | Error err -> return Error err
            | Ok dashboardIdObj ->
                let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                match dashboardOption with
                | None -> return Error(NotFound "Dashboard not found")
                | Some dashboard ->
                    match dashboard.State.PrepareAppId with
                    | None -> return Error(InvalidOperation "Dashboard is not configured with a prepare app")
                    | Some prepareAppId ->
                        match parseDashboardConfiguration dashboard.State.Configuration with
                        | Error err -> return Error err
                        | Ok(_, bindings) ->
                            match buildRunInputs prepareAppId None dto.LoadInputs [] None bindings with
                            | Error err -> return Error err
                            | Ok runInputs ->
                                let! runResult =
                                    executeRunAsync
                                        runRepository
                                        appRepository
                                        resourceRepository
                                        sqlExecutionService
                                        actorUserId
                                        prepareAppId
                                        currentUser
                                        runInputs

                                match runResult with
                                | Error runError ->
                                    let failureEvent =
                                        DashboardEvents.dashboardPrepareFailed
                                            actorUserId
                                            dashboard.State.Id
                                            (Some prepareAppId)
                                            (runError.ToString())

                                    let! eventSaveResult =
                                        saveRuntimeEventAsync eventRepository (failureEvent :> IDomainEvent)

                                    match eventSaveResult with
                                    | Error eventError -> return Error eventError
                                    | Ok() -> return Error runError
                                | Ok runData ->
                                    match runData.Status with
                                    | RunStatus.Success ->
                                        let preparedEvent =
                                            DashboardEvents.dashboardPrepared
                                                actorUserId
                                                dashboard.State.Id
                                                prepareAppId
                                                runData.Id

                                        let! eventSaveResult =
                                            saveRuntimeEventAsync eventRepository (preparedEvent :> IDomainEvent)

                                        match eventSaveResult with
                                        | Error eventError -> return Error eventError
                                        | Ok() ->
                                            return
                                                Ok(
                                                    DashboardPrepareResult {
                                                        PrepareRunId = runData.Id.Value.ToString()
                                                        Status = runData.Status.ToString()
                                                        Response = runData.Response
                                                        ErrorMessage = runData.ErrorMessage
                                                    }
                                                )
                                    | _ ->
                                        let failureMessage =
                                            runData.ErrorMessage
                                            |> Option.defaultValue
                                                $"Prepare run ended with status {runData.Status.ToString()}"

                                        let failureEvent =
                                            DashboardEvents.dashboardPrepareFailed
                                                actorUserId
                                                dashboard.State.Id
                                                (Some prepareAppId)
                                                failureMessage

                                        let! eventSaveResult =
                                            saveRuntimeEventAsync eventRepository (failureEvent :> IDomainEvent)

                                        match eventSaveResult with
                                        | Error eventError -> return Error eventError
                                        | Ok() -> return Error(InvalidOperation failureMessage)
        }

    let private handleRunDashboardAction
        (dashboardRepository: IDashboardRepository)
        (runRepository: IRunRepository)
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (sqlExecutionService: ISqlExecutionService)
        (eventRepository: IEventRepository)
        (actorUserId: UserId)
        (dashboardId: string)
        (actionId: ActionId)
        (currentUser: CurrentUser)
        (dto: RunDashboardActionDto)
        : Task<Result<DashboardCommandResult, DomainError>> =
        task {
            if
                dto.PriorActionRunIds
                |> Option.exists (fun priorRuns ->
                    priorRuns
                    |> List.exists (fun priorRun -> not (String.IsNullOrWhiteSpace(priorRun.Key))))
            then
                return Error(ValidationError "priorActionRunIds are not supported in dashboard runtime v1")
            else
                match parseDashboardId dashboardId with
                | Error err -> return Error err
                | Ok dashboardIdObj ->
                    let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                    match dashboardOption with
                    | None -> return Error(NotFound "Dashboard not found")
                    | Some dashboard ->
                        match parseDashboardConfiguration dashboard.State.Configuration with
                        | Error err -> return Error err
                        | Ok(actionMap, bindings) ->
                            match actionMap.TryFind actionId with
                            | None -> return Error(NotFound $"Dashboard action '{actionId.Value}' was not found")
                            | Some actionDefinition ->
                                let! prepareResponseResult = task {
                                    match dashboard.State.PrepareAppId, dto.PrepareRunId with
                                    | Some _, None ->
                                        return
                                            Error(
                                                ValidationError
                                                    "prepareRunId is required to run dashboard actions when prepare app is configured"
                                            )
                                    | None, Some _ ->
                                        return
                                            Error(
                                                ValidationError
                                                    "prepareRunId was provided but dashboard has no prepare app"
                                            )
                                    | None, None -> return Ok None
                                    | Some prepareAppId, Some prepareRunIdValue ->
                                        match parseRunId prepareRunIdValue with
                                        | Error err -> return Error err
                                        | Ok prepareRunId ->
                                            let! prepareRunOption = runRepository.GetByIdAsync prepareRunId

                                            match prepareRunOption with
                                            | None ->
                                                return
                                                    Error(NotFound $"Prepare run '{prepareRunId.Value}' was not found")
                                            | Some prepareRun when Run.getAppId prepareRun <> prepareAppId ->
                                                return
                                                    Error(
                                                        ValidationError
                                                            "prepareRunId does not belong to this dashboard prepare app"
                                                    )
                                            | Some prepareRun when prepareRun.State.Status <> RunStatus.Success ->
                                                return
                                                    Error(
                                                        ValidationError
                                                            "prepareRunId must reference a successful prepare run"
                                                    )
                                            | Some prepareRun -> return Ok(Run.getResponse prepareRun)
                                }

                                match prepareResponseResult with
                                | Error err -> return Error err
                                | Ok prepareResponse ->
                                    match
                                        buildRunInputs
                                            actionDefinition.AppId
                                            (Some actionId)
                                            dto.LoadInputs
                                            dto.ActionInputs
                                            prepareResponse
                                            bindings
                                    with
                                    | Error err -> return Error err
                                    | Ok runInputs ->
                                        let! runResult =
                                            executeRunAsync
                                                runRepository
                                                appRepository
                                                resourceRepository
                                                sqlExecutionService
                                                actorUserId
                                                actionDefinition.AppId
                                                currentUser
                                                runInputs

                                        match runResult with
                                        | Error runError ->
                                            let failedEvent =
                                                DashboardEvents.dashboardActionFailed
                                                    actorUserId
                                                    dashboard.State.Id
                                                    actionId
                                                    (Some actionDefinition.AppId)
                                                    (runError.ToString())

                                            let! eventSaveResult =
                                                saveRuntimeEventAsync eventRepository (failedEvent :> IDomainEvent)

                                            match eventSaveResult with
                                            | Error eventError -> return Error eventError
                                            | Ok() -> return Error runError
                                        | Ok runData ->
                                            let! eventSaveResult =
                                                match runData.Status with
                                                | RunStatus.Success ->
                                                    let successEvent =
                                                        DashboardEvents.dashboardActionExecuted
                                                            actorUserId
                                                            dashboard.State.Id
                                                            actionId
                                                            actionDefinition.AppId
                                                            runData.Id

                                                    saveRuntimeEventAsync eventRepository (successEvent :> IDomainEvent)
                                                | _ ->
                                                    let failureMessage =
                                                        runData.ErrorMessage
                                                        |> Option.defaultValue
                                                            $"Action run ended with status {runData.Status.ToString()}"

                                                    let failedEvent =
                                                        DashboardEvents.dashboardActionFailed
                                                            actorUserId
                                                            dashboard.State.Id
                                                            actionId
                                                            (Some actionDefinition.AppId)
                                                            failureMessage

                                                    saveRuntimeEventAsync eventRepository (failedEvent :> IDomainEvent)

                                            match eventSaveResult with
                                            | Error eventError -> return Error eventError
                                            | Ok() ->
                                                return
                                                    Ok(
                                                        DashboardActionResult {
                                                            ActionRunId = runData.Id.Value.ToString()
                                                            Status = runData.Status.ToString()
                                                            Response = runData.Response
                                                            ErrorMessage = runData.ErrorMessage
                                                        }
                                                    )
        }

    let handleCommand
        (dashboardRepository: IDashboardRepository)
        (runRepository: IRunRepository)
        (appRepository: IAppRepository)
        (resourceRepository: IResourceRepository)
        (sqlExecutionService: ISqlExecutionService)
        (eventRepository: IEventRepository)
        (command: DashboardCommand)
        : Task<Result<DashboardCommandResult, DomainError>> =
        task {
            match command with
            | CreateDashboard(actorUserId, dto) ->
                match parseFolderId dto.FolderId, parsePrepareAppId dto.PrepareAppId with
                | Error err, _ -> return Error err
                | _, Error err -> return Error err
                | Ok folderId, Ok prepareAppId ->
                    match Dashboard.create actorUserId dto.Name folderId prepareAppId dto.Configuration with
                    | Error err -> return Error err
                    | Ok dashboard ->
                        let! exists =
                            dashboardRepository.ExistsByNameAndFolderIdAsync
                                dashboard.State.Name
                                dashboard.State.FolderId

                        if exists then
                            return Error(Conflict "A dashboard with this name already exists in the folder")
                        else
                            match! dashboardRepository.AddAsync dashboard with
                            | Error err -> return Error err
                            | Ok() -> return Ok(DashboardResult dashboard.State)

            | GetDashboardById dashboardId ->
                match parseDashboardId dashboardId with
                | Error err -> return Error err
                | Ok dashboardIdObj ->
                    let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                    match dashboardOption with
                    | None -> return Error(NotFound "Dashboard not found")
                    | Some dashboard -> return Ok(DashboardResult dashboard.State)

            | GetDashboardsByFolderId(folderId, skip, take) ->
                match parseFolderId folderId, validatePagination skip take with
                | Error err, _ -> return Error err
                | _, Error err -> return Error err
                | Ok folderIdObj, Ok() ->
                    let! dashboards = dashboardRepository.GetByFolderIdAsync folderIdObj skip take
                    let! totalCount = dashboardRepository.GetCountByFolderIdAsync folderIdObj

                    let result: PagedResult<DashboardData> = {
                        Items = dashboards |> List.map (fun dashboard -> dashboard.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(DashboardsResult result)

            | GetAllDashboards(skip, take) ->
                match validatePagination skip take with
                | Error err -> return Error err
                | Ok() ->
                    let! dashboards = dashboardRepository.GetAllAsync skip take
                    let! totalCount = dashboardRepository.GetCountAsync()

                    let result: PagedResult<DashboardData> = {
                        Items = dashboards |> List.map (fun dashboard -> dashboard.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(DashboardsResult result)

            | GetDashboardsBySpaceIds(spaceIds, skip, take) ->
                match validatePagination skip take with
                | Error err -> return Error err
                | Ok() ->
                    if List.isEmpty spaceIds then
                        return
                            Ok(
                                DashboardsResult {
                                    Items = []
                                    TotalCount = 0
                                    Skip = skip
                                    Take = take
                                }
                            )
                    else
                        let! dashboards = dashboardRepository.GetBySpaceIdsAsync spaceIds skip take
                        let! totalCount = dashboardRepository.GetCountBySpaceIdsAsync spaceIds

                        let result: PagedResult<DashboardData> = {
                            Items = dashboards |> List.map (fun dashboard -> dashboard.State)
                            TotalCount = totalCount
                            Skip = skip
                            Take = take
                        }

                        return Ok(DashboardsResult result)

            | UpdateDashboardName(actorUserId, dashboardId, dto) ->
                match parseDashboardId dashboardId with
                | Error err -> return Error err
                | Ok dashboardIdObj ->
                    let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                    match dashboardOption with
                    | None -> return Error(NotFound "Dashboard not found")
                    | Some dashboard ->
                        if dashboard.State.Name.Value <> dto.Name then
                            match DashboardName.Create(Some dto.Name) with
                            | Error err -> return Error err
                            | Ok newName ->
                                let! exists =
                                    dashboardRepository.ExistsByNameAndFolderIdAsync newName dashboard.State.FolderId

                                if exists then
                                    return Error(Conflict "A dashboard with this name already exists in the folder")
                                else
                                    match Dashboard.updateName actorUserId dto.Name dashboard with
                                    | Error err -> return Error err
                                    | Ok updatedDashboard ->
                                        match! dashboardRepository.UpdateAsync updatedDashboard with
                                        | Error err -> return Error err
                                        | Ok() -> return Ok(DashboardResult updatedDashboard.State)
                        else
                            return Ok(DashboardResult dashboard.State)

            | UpdateDashboardConfiguration(actorUserId, dashboardId, dto) ->
                match parseDashboardId dashboardId with
                | Error err -> return Error err
                | Ok dashboardIdObj ->
                    let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                    match dashboardOption with
                    | None -> return Error(NotFound "Dashboard not found")
                    | Some dashboard ->
                        let updatedDashboard =
                            Dashboard.updateConfiguration actorUserId dto.Configuration dashboard

                        match! dashboardRepository.UpdateAsync updatedDashboard with
                        | Error err -> return Error err
                        | Ok() -> return Ok(DashboardResult updatedDashboard.State)

            | UpdateDashboardPrepareApp(actorUserId, dashboardId, dto) ->
                match parseDashboardId dashboardId, parsePrepareAppId dto.PrepareAppId with
                | Error err, _ -> return Error err
                | _, Error err -> return Error err
                | Ok dashboardIdObj, Ok prepareAppId ->
                    let! dashboardOption = dashboardRepository.GetByIdAsync dashboardIdObj

                    match dashboardOption with
                    | None -> return Error(NotFound "Dashboard not found")
                    | Some dashboard ->
                        let updatedDashboard = Dashboard.updatePrepareApp actorUserId prepareAppId dashboard

                        match! dashboardRepository.UpdateAsync updatedDashboard with
                        | Error err -> return Error err
                        | Ok() -> return Ok(DashboardResult updatedDashboard.State)

            | PrepareDashboard(actorUserId, dashboardId, currentUser, dto) ->
                return!
                    handlePrepareDashboard
                        dashboardRepository
                        runRepository
                        appRepository
                        resourceRepository
                        sqlExecutionService
                        eventRepository
                        actorUserId
                        dashboardId
                        currentUser
                        dto

            | RunDashboardAction(actorUserId, dashboardId, actionId, currentUser, dto) ->
                return!
                    handleRunDashboardAction
                        dashboardRepository
                        runRepository
                        appRepository
                        resourceRepository
                        sqlExecutionService
                        eventRepository
                        actorUserId
                        dashboardId
                        actionId
                        currentUser
                        dto

            | DeleteDashboard(actorUserId, dashboardId) ->
                match parseDashboardId dashboardId with
                | Error err -> return Error err
                | Ok dashboardIdObj ->
                    match! dashboardRepository.DeleteAsync dashboardIdObj actorUserId with
                    | Error err -> return Error err
                    | Ok() -> return Ok(DashboardUnitResult())
        }

type DashboardHandler
    (
        dashboardRepository: IDashboardRepository,
        runRepository: IRunRepository,
        appRepository: IAppRepository,
        resourceRepository: IResourceRepository,
        sqlExecutionService: ISqlExecutionService,
        eventRepository: IEventRepository
    ) =
    interface ICommandHandler<DashboardCommand, DashboardCommandResult> with
        member _.HandleCommand(command) =
            DashboardHandler.handleCommand
                dashboardRepository
                runRepository
                appRepository
                resourceRepository
                sqlExecutionService
                eventRepository
                command