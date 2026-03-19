namespace Freetool.Domain.Services

open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects

module RequestComposer =
    /// Combines a Resource and an App to create an ExecutableHttpRequest
    /// The App's ResourceId should match the Resource's Id for proper composition
    let composeExecutableRequest
        (resource: ValidatedResource)
        (app: ValidatedApp)
        : Result<ExecutableHttpRequest, DomainError> =
        // Verify that the app references this resource
        let appResourceId = App.getResourceId app
        let resourceId = Resource.getId resource

        if appResourceId <> resourceId then
            Error(
                InvalidOperation $"App references ResourceId {appResourceId} but provided Resource has Id {resourceId}"
            )
        else
            // Get resource data
            match Resource.getResourceKind resource with
            | ResourceKind.Sql -> Error(InvalidOperation "SQL resources cannot be used for HTTP requests")
            | ResourceKind.Http ->
                match Resource.getBaseUrl resource with
                | None -> Error(InvalidOperation "HTTP resource is missing a base URL")
                | Some resourceBaseUrl ->
                    let resourceUrlParams = Resource.getUrlParameters resource
                    let resourceHeaders = Resource.getHeaders resource
                    let resourceBody = Resource.getBody resource

                    // Get app data
                    let httpMethod = App.getHttpMethod app
                    let appUrlPath = App.getUrlPath app
                    let appUrlParameters = App.getUrlParameters app
                    let appHeaders = App.getHeaders app
                    let appBody = App.getBody app

                    // Compose base URL by combining resource base URL with app's URL path
                    let composedBaseUrl =
                        match appUrlPath with
                        | None -> resourceBaseUrl
                        | Some path ->
                            // Ensure proper URL composition
                            let trimmedBaseUrl = resourceBaseUrl.TrimEnd('/')
                            let trimmedPath = path.TrimStart('/')

                            if trimmedPath = "" then
                                trimmedBaseUrl
                            else
                                $"{trimmedBaseUrl}/{trimmedPath}"

                    // Merge URL parameters (app can only add new parameters, cannot override existing resource parameters)
                    let mergedUrlParameters =
                        let resourceUrlParamsDict = resourceUrlParams |> Map.ofList
                        let appUrlParamsDict = appUrlParameters |> Map.ofList

                        // Check for conflicts where app tries to override resource parameters
                        let conflicts =
                            appUrlParamsDict
                            |> Map.toList
                            |> List.filter (fun (key, _) -> resourceUrlParamsDict.ContainsKey(key))

                        if not conflicts.IsEmpty then
                            let conflictKeys = conflicts |> List.map fst |> String.concat ", "

                            Error(
                                InvalidOperation $"App cannot override existing Resource URL parameters: {conflictKeys}"
                            )
                        else
                            // Only add new parameters from app
                            let combinedParams = (resourceUrlParams @ appUrlParameters) |> List.distinctBy fst
                            Ok combinedParams

                    match mergedUrlParameters with
                    | Error error -> Error error
                    | Ok urlParams ->
                        // Merge headers (app can only add new headers, cannot override existing resource headers)
                        let mergedHeaders =
                            let resourceHeaderDict = resourceHeaders |> Map.ofList
                            let appHeaderDict = appHeaders |> Map.ofList

                            // Check for conflicts where app tries to override resource headers
                            let conflicts =
                                appHeaderDict
                                |> Map.toList
                                |> List.filter (fun (key, _) -> resourceHeaderDict.ContainsKey(key))

                            if not conflicts.IsEmpty then
                                let conflictKeys = conflicts |> List.map fst |> String.concat ", "
                                Error(InvalidOperation $"App cannot override existing Resource headers: {conflictKeys}")
                            else
                                // Only add new headers from app
                                let combinedHeaders = (resourceHeaders @ appHeaders) |> List.distinctBy fst
                                Ok combinedHeaders

                        match mergedHeaders with
                        | Error error -> Error error
                        | Ok headers ->
                            // Merge body (app can only add new body parameters, cannot override existing resource body parameters)
                            let mergedBody =
                                let resourceBodyDict = resourceBody |> Map.ofList
                                let appBodyDict = appBody |> Map.ofList

                                // Check for conflicts where app tries to override resource body parameters
                                let conflicts =
                                    appBodyDict
                                    |> Map.toList
                                    |> List.filter (fun (key, _) -> resourceBodyDict.ContainsKey(key))

                                if not conflicts.IsEmpty then
                                    let conflictKeys = conflicts |> List.map fst |> String.concat ", "

                                    Error(
                                        InvalidOperation
                                            $"App cannot override existing Resource body parameters: {conflictKeys}"
                                    )
                                else
                                    // Only add new body parameters from app
                                    let combinedBody = (resourceBody @ appBody) |> List.distinctBy fst
                                    Ok combinedBody

                            match mergedBody with
                            | Error error -> Error error
                            | Ok body ->
                                // Return composed ExecutableHttpRequest
                                // UseJsonBody is set to false by default; it will be overridden
                                // in Run.composeExecutableRequestFromAppAndResource if needed
                                Ok {
                                    BaseUrl = composedBaseUrl
                                    UrlParameters = urlParams
                                    Headers = headers
                                    Body = body
                                    HttpMethod = httpMethod
                                    UseJsonBody = false
                                }