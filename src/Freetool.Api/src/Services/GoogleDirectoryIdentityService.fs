namespace Freetool.Api.Services

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Google.Apis.Auth.OAuth2
open Freetool.Api

type GoogleDirectoryIdentityData = {
    GroupKeys: string list
    ProfilePicUrl: string option
}

type IGoogleDirectoryIdentityService =
    abstract member GetIdentityGroupKeysAsync: email: string -> Task<string list>
    abstract member GetIdentityDataAsync: email: string -> Task<GoogleDirectoryIdentityData>

type GoogleDirectoryIdentityService
    (
        configuration: IConfiguration,
        httpClientFactory: IHttpClientFactory,
        logger: ILogger<GoogleDirectoryIdentityService>
    ) =

    let defaultScope = "https://www.googleapis.com/auth/admin.directory.user.readonly"

    let tryParseBool (value: string option) (fallbackValue: bool) =
        match value with
        | Some rawValue ->
            match Boolean.TryParse rawValue with
            | true, parsedValue -> parsedValue
            | false, _ -> fallbackValue
        | None -> fallbackValue

    let normalizeValues (values: string list) =
        values
        |> List.map (fun v -> v.Trim())
        |> List.filter (fun v -> not (String.IsNullOrWhiteSpace v))

    let rec extractValuesFromJsonElement (element: JsonElement) : string list =
        match element.ValueKind with
        | JsonValueKind.String ->
            match element.GetString() with
            | null -> []
            | value -> [ value ]
        | JsonValueKind.Number
        | JsonValueKind.True
        | JsonValueKind.False -> [ element.GetRawText() ]
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.collect extractValuesFromJsonElement
            |> Seq.toList
        | JsonValueKind.Object ->
            match element.TryGetProperty("value") with
            | true, valueElement -> extractValuesFromJsonElement valueElement
            | _ ->
                element.EnumerateObject()
                |> Seq.collect (fun property -> extractValuesFromJsonElement property.Value)
                |> Seq.toList
        | _ -> []

    let buildOuHierarchyKeys (prefix: string) (orgUnitPath: string) =
        let normalizedPath = orgUnitPath.Trim()

        if String.IsNullOrWhiteSpace normalizedPath then
            []
        else
            let segments =
                normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList

            if List.isEmpty segments then
                [ $"{prefix}:{normalizedPath}" ]
            else
                [
                    for index in 1 .. segments.Length do
                        let partialPath = "/" + (segments |> List.take index |> String.concat "/")
                        $"{prefix}:{partialPath}"
                ]

    let getAccessTokenAsync (scope: string) (adminUserEmail: string option) = task {
        let credentialsFile =
            configuration[ConfigurationKeys.Auth.GoogleDirectory.CredentialsFile]
            |> Option.ofObj
            |> Option.map (fun value -> value.Trim())
            |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))

        let! credential =
            match credentialsFile with
            | Some path -> GoogleCredential.FromFileAsync(path, CancellationToken.None)
            | None -> GoogleCredential.GetApplicationDefaultAsync()

        let scopedCredential = credential.CreateScoped([ scope ])

        let delegatedCredential =
            match adminUserEmail with
            | Some adminUser when not (String.IsNullOrWhiteSpace adminUser) ->
                scopedCredential.CreateWithUser(adminUser)
            | _ -> scopedCredential

        let tokenAccess: ITokenAccess = delegatedCredential.UnderlyingCredential
        let! accessToken = tokenAccess.GetAccessTokenForRequestAsync()

        if String.IsNullOrWhiteSpace accessToken then
            return Error "Received empty access token from ADC credentials"
        else
            return Ok accessToken
    }

    let emptyIdentityData = { GroupKeys = []; ProfilePicUrl = None }

    let tryGetStringProperty (propertyName: string) (element: JsonElement) =
        match element.TryGetProperty(propertyName) with
        | true, prop when prop.ValueKind = JsonValueKind.String ->
            let value = prop.GetString()

            if String.IsNullOrWhiteSpace value then
                None
            else
                Some(value.Trim())
        | _ -> None

    let getDirectoryIdentityDataAsync (email: string) = task {
        let enabled =
            configuration[ConfigurationKeys.Auth.GoogleDirectory.Enabled]
            |> Option.ofObj
            |> fun value -> tryParseBool value false

        if not enabled then
            return emptyIdentityData
        else
            let adminUserEmail =
                configuration[ConfigurationKeys.Auth.GoogleDirectory.AdminUserEmail]
                |> Option.ofObj

            let scope =
                configuration[ConfigurationKeys.Auth.GoogleDirectory.Scope]
                |> Option.ofObj
                |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))
                |> Option.defaultValue defaultScope

            let customKeyPrefix =
                configuration[ConfigurationKeys.Auth.GoogleDirectory.CustomAttributeKeyPrefix]
                |> Option.ofObj
                |> Option.defaultValue "custom"

            let ouKeyPrefix =
                configuration[ConfigurationKeys.Auth.GoogleDirectory.OrgUnitKeyPrefix]
                |> Option.ofObj
                |> Option.defaultValue "ou"

            let includeOrgUnitHierarchy =
                configuration[ConfigurationKeys.Auth.GoogleDirectory.IncludeOrgUnitHierarchy]
                |> Option.ofObj
                |> fun value -> tryParseBool value true

            let encodedEmail = Uri.EscapeDataString(email)

            let requestUrl =
                $"https://admin.googleapis.com/admin/directory/v1/users/{encodedEmail}?projection=FULL"

            let! tokenResult = getAccessTokenAsync scope adminUserEmail

            match tokenResult with
            | Error errorMessage ->
                logger.LogWarning(
                    "Failed to obtain Google Directory access token for {Email}: {Error}",
                    email,
                    errorMessage
                )

                return emptyIdentityData
            | Ok token ->
                try
                    use client = httpClientFactory.CreateClient()
                    use request = new HttpRequestMessage(HttpMethod.Get, requestUrl)
                    request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)

                    let! response = client.SendAsync(request)

                    if not response.IsSuccessStatusCode then
                        let! body = response.Content.ReadAsStringAsync()

                        logger.LogWarning(
                            "Google Directory lookup failed for {Email}. Status={StatusCode}, Body={Body}",
                            email,
                            int response.StatusCode,
                            body
                        )

                        return emptyIdentityData
                    else
                        let! body = response.Content.ReadAsStringAsync()
                        use json = JsonDocument.Parse(body)

                        let keys = ResizeArray<string>()

                        match json.RootElement.TryGetProperty("orgUnitPath") with
                        | true, orgUnitElement when orgUnitElement.ValueKind = JsonValueKind.String ->
                            let orgUnitPath = orgUnitElement.GetString()

                            if not (String.IsNullOrWhiteSpace orgUnitPath) then
                                keys.Add($"{ouKeyPrefix}:{orgUnitPath}")

                                if includeOrgUnitHierarchy then
                                    for key in buildOuHierarchyKeys ouKeyPrefix orgUnitPath do
                                        keys.Add(key)
                        | _ -> ()

                        match json.RootElement.TryGetProperty("customSchemas") with
                        | true, customSchemasElement when customSchemasElement.ValueKind = JsonValueKind.Object ->
                            for schemaProperty in customSchemasElement.EnumerateObject() do
                                if schemaProperty.Value.ValueKind = JsonValueKind.Object then
                                    for fieldProperty in schemaProperty.Value.EnumerateObject() do
                                        let baseKey = $"{customKeyPrefix}:{schemaProperty.Name}.{fieldProperty.Name}"

                                        keys.Add(baseKey)

                                        let values =
                                            fieldProperty.Value |> extractValuesFromJsonElement |> normalizeValues

                                        for value in values do
                                            keys.Add($"{baseKey}:{value}")
                        | _ -> ()

                        let directoryProfilePicUrl =
                            tryGetStringProperty "thumbnailPhotoUrl" json.RootElement
                            |> Option.orElseWith (fun () -> tryGetStringProperty "photoUrl" json.RootElement)

                        return {
                            GroupKeys = keys |> Seq.distinct |> Seq.toList
                            ProfilePicUrl = directoryProfilePicUrl
                        }
                with ex ->
                    logger.LogWarning(
                        "Google Directory lookup failed unexpectedly for {Email}: {Error}",
                        email,
                        ex.Message
                    )

                    return emptyIdentityData
    }

    interface IGoogleDirectoryIdentityService with
        member _.GetIdentityGroupKeysAsync(email: string) : Task<string list> = task {
            let! identityData = getDirectoryIdentityDataAsync email
            return identityData.GroupKeys
        }

        member _.GetIdentityDataAsync(email: string) : Task<GoogleDirectoryIdentityData> =
            getDirectoryIdentityDataAsync email