namespace Freetool.Domain

type AppResourceConflictData = {
    AppId: string
    UrlParameters: (string * string) list
    Headers: (string * string) list
    Body: (string * string) list
}

type ResourceAppConflictData = {
    UrlParameters: (string * string) list
    Headers: (string * string) list
    Body: (string * string) list
}

module BusinessRules =
    let checkAppToResourceConflicts
        (apps: AppResourceConflictData list)
        (newUrlParameters: (string * string) list option)
        (newHeaders: (string * string) list option)
        (newBody: (string * string) list option)
        : Result<unit, DomainError> =

        let allConflicts =
            apps
            |> List.collect (fun app ->
                [
                    DomainValidation.checkKeyValueConflicts
                        newUrlParameters
                        (Some app.UrlParameters)
                        $"App {app.AppId} URL parameters"
                    DomainValidation.checkKeyValueConflicts newHeaders (Some app.Headers) $"App {app.AppId} Headers"
                    DomainValidation.checkKeyValueConflicts newBody (Some app.Body) $"App {app.AppId} Body parameters"
                ]
                |> List.choose id)

        if not allConflicts.IsEmpty then
            let combinedMessage = String.concat "; " allConflicts
            Error(InvalidOperation $"Resource cannot override existing App values: {combinedMessage}")
        else
            Ok()

    let checkResourceToAppConflicts
        (resource: ResourceAppConflictData)
        (newUrlParameters: (string * string) list option)
        (newHeaders: (string * string) list option)
        (newBody: (string * string) list option)
        : Result<unit, DomainError> =

        let allConflicts =
            [
                DomainValidation.checkKeyValueConflicts (Some resource.UrlParameters) newUrlParameters "URL parameters"
                DomainValidation.checkKeyValueConflicts (Some resource.Headers) newHeaders "Headers"
                DomainValidation.checkKeyValueConflicts (Some resource.Body) newBody "Body parameters"
            ]
            |> List.choose id

        if not allConflicts.IsEmpty then
            let combinedMessage = String.concat "; " allConflicts
            Error(InvalidOperation $"App cannot override existing Resource values: {combinedMessage}")
        else
            Ok()