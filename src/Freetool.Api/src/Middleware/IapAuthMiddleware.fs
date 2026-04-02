namespace Freetool.Api.Middleware

open System
open System.Threading.Tasks
open System.Threading
open System.Diagnostics
open System.Net.Http
open System.Security.Claims
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open System.IdentityModel.Tokens.Jwt
open Microsoft.IdentityModel.Tokens
open Freetool.Api
open Freetool.Api.Auth
open Freetool.Api.Services
open Freetool.Api.Tracing

type JwtValidationError =
    | MissingJwtHeader of string
    | Misconfigured of string
    | KeyFetchFailed of string
    | InvalidToken of string

type IapAuthMiddleware(next: RequestDelegate, logger: ILogger<IapAuthMiddleware>) =
    let keyCacheSemaphore = new SemaphoreSlim(1, 1)
    let mutable cachedSigningKeys: (SecurityKey list * DateTimeOffset) option = None

    let defaultValidateJwt = true
    let defaultJwtAssertionHeader = "X-Goog-Iap-Jwt-Assertion"
    let defaultJwtIssuer = "https://cloud.google.com/iap"
    let defaultJwtCertsUrl = "https://www.gstatic.com/iap/verify/public_key-jwk"

    let defaultPlatformJwtAudienceSetting =
        ConfigurationKeys.Auth.IAP.PlatformJwtAudience

    let defaultEmailHeader = "X-Goog-Authenticated-User-Email"
    let defaultNameHeader = "X-Goog-Authenticated-User-Name"
    let defaultPictureHeader = "X-Goog-Iap-Attr-Picture"
    let defaultGroupsHeader = "X-Goog-Iap-Attr-Groups"
    let defaultGroupsDelimiter = ","

    let extractHeader (headerKey: string) (context: HttpContext) : string option =
        match context.Request.Headers.TryGetValue headerKey with
        | true, values when values.Count > 0 ->
            let value = values.[0]

            if String.IsNullOrWhiteSpace value then None else Some value
        | _ -> None

    let parseEmailValue (rawValue: string) =
        // IAP may send values like "accounts.google.com:user@company.com"
        let separatorIndex = rawValue.IndexOf(":")

        if separatorIndex >= 0 && separatorIndex < rawValue.Length - 1 then
            rawValue.Substring(separatorIndex + 1)
        else
            rawValue

    let parseGroups (rawGroups: string option) (delimiter: string) =
        let actualDelimiter =
            if String.IsNullOrWhiteSpace delimiter then
                defaultGroupsDelimiter
            else
                delimiter

        rawGroups
        |> Option.defaultValue ""
        |> fun value ->
            value.Split(actualDelimiter, StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.distinct

    let tryParseBool (value: string option) (fallbackValue: bool) =
        match value with
        | Some rawValue ->
            match Boolean.TryParse rawValue with
            | true, parsedValue -> parsedValue
            | false, _ -> fallbackValue
        | None -> fallbackValue

    let getConfiguredString (configuration: IConfiguration) (key: string) =
        configuration[key]
        |> Option.ofObj
        |> Option.bind (fun value -> if String.IsNullOrWhiteSpace value then None else Some value)

    let findEmailClaim (principal: ClaimsPrincipal) =
        [ "email"; ClaimTypes.Email ]
        |> List.tryPick (fun claimType ->
            principal.FindFirst(claimType)
            |> Option.ofObj
            |> Option.map (fun claim -> claim.Value))
        |> Option.bind (fun value -> if String.IsNullOrWhiteSpace value then None else Some value)

    let getCachedSigningKeys () =
        let now = DateTimeOffset.UtcNow

        match cachedSigningKeys with
        | Some(signingKeys, expiresAt) when expiresAt > now -> Some signingKeys
        | _ -> None

    let refreshSigningKeysAsync (httpClientFactory: IHttpClientFactory) (jwtCertsUrl: string) = task {
        try
            use client = httpClientFactory.CreateClient()
            let! response = client.GetAsync(jwtCertsUrl)
            response.EnsureSuccessStatusCode() |> ignore
            let! content = response.Content.ReadAsStringAsync()

            let keySet = JsonWebKeySet(content)
            let signingKeys = keySet.GetSigningKeys() |> Seq.toList

            if List.isEmpty signingKeys then
                return Error "No signing keys returned by IAP certs endpoint"
            else
                // Google rotates keys infrequently; a 1-hour cache keeps validation fast while remaining fresh.
                cachedSigningKeys <- Some(signingKeys, DateTimeOffset.UtcNow.AddHours(1.0))
                return Ok signingKeys
        with ex ->
            return Error ex.Message
    }

    let getSigningKeysAsync (httpClientFactory: IHttpClientFactory) (jwtCertsUrl: string) = task {
        match getCachedSigningKeys () with
        | Some signingKeys -> return Ok signingKeys
        | None ->
            do! keyCacheSemaphore.WaitAsync()

            try
                match getCachedSigningKeys () with
                | Some signingKeys -> return Ok signingKeys
                | None -> return! refreshSigningKeysAsync httpClientFactory jwtCertsUrl
            finally
                keyCacheSemaphore.Release() |> ignore
    }

    let validateJwtToken
        (jwtAssertion: string)
        (jwtAudience: string)
        (jwtIssuer: string)
        (signingKeys: SecurityKey list)
        =
        try
            let handler = JwtSecurityTokenHandler()
            handler.MapInboundClaims <- false

            let parsedToken = handler.ReadJwtToken(jwtAssertion)

            let isSupportedAlgorithm =
                parsedToken.Header.Alg = SecurityAlgorithms.EcdsaSha256
                || parsedToken.Header.Alg = SecurityAlgorithms.RsaSha256

            if not isSupportedAlgorithm then
                Error $"Unsupported JWT algorithm '{parsedToken.Header.Alg}'. Expected ES256."
            else
                let validationParameters = TokenValidationParameters()
                validationParameters.ValidateIssuerSigningKey <- true
                validationParameters.IssuerSigningKeys <- signingKeys
                validationParameters.ValidateIssuer <- true
                validationParameters.ValidIssuer <- jwtIssuer
                validationParameters.ValidateAudience <- true
                validationParameters.ValidAudience <- jwtAudience
                validationParameters.ValidateLifetime <- true
                validationParameters.RequireSignedTokens <- true
                validationParameters.RequireExpirationTime <- true
                validationParameters.ClockSkew <- TimeSpan.FromMinutes(2.0)

                let mutable validatedToken: SecurityToken = null

                let principal =
                    handler.ValidateToken(jwtAssertion, validationParameters, &validatedToken)

                Ok principal
        with ex ->
            Error ex.Message

    let validateIapJwtAsync (context: HttpContext) (configuration: IConfiguration) = task {
        let jwtAssertionHeader =
            configuration[ConfigurationKeys.Auth.IAP.JwtAssertionHeader]
            |> Option.ofObj
            |> Option.defaultValue defaultJwtAssertionHeader

        let jwtAudience =
            getConfiguredString configuration ConfigurationKeys.Auth.IAP.JwtAudience
            |> Option.orElseWith (fun () -> getConfiguredString configuration defaultPlatformJwtAudienceSetting)

        let jwtIssuer =
            configuration[ConfigurationKeys.Auth.IAP.JwtIssuer]
            |> Option.ofObj
            |> Option.defaultValue defaultJwtIssuer

        let jwtCertsUrl =
            configuration[ConfigurationKeys.Auth.IAP.JwtCertsUrl]
            |> Option.ofObj
            |> Option.defaultValue defaultJwtCertsUrl

        match extractHeader jwtAssertionHeader context with
        | None -> return Error(MissingJwtHeader jwtAssertionHeader)
        | Some jwtAssertion ->
            match jwtAudience with
            | None
            | Some "" -> return Error(Misconfigured "Auth:IAP:JwtAudience is required when JWT validation is enabled")
            | Some audience when String.IsNullOrWhiteSpace audience ->
                return Error(Misconfigured "Auth:IAP:JwtAudience is required when JWT validation is enabled")
            | Some audience ->
                let httpClientFactory =
                    context.RequestServices.GetRequiredService<IHttpClientFactory>()

                let! signingKeysResult = getSigningKeysAsync httpClientFactory jwtCertsUrl

                match signingKeysResult with
                | Error message -> return Error(KeyFetchFailed message)
                | Ok signingKeys ->
                    return
                        match validateJwtToken jwtAssertion audience jwtIssuer signingKeys with
                        | Ok principal -> Ok principal
                        | Error message -> Error(InvalidToken message)
    }

    member _.InvokeAsync(context: HttpContext) : Task = task {
        let requestPath = context.Request.Path.Value

        if
            not (String.IsNullOrWhiteSpace(requestPath))
            && String.Equals(requestPath, "/healthy", StringComparison.OrdinalIgnoreCase)
        then
            do! next.Invoke context
        else
            let currentActivity = Option.ofObj Activity.Current
            let configuration = context.RequestServices.GetRequiredService<IConfiguration>()

            let validateJwt =
                configuration[ConfigurationKeys.Auth.IAP.ValidateJwt]
                |> Option.ofObj
                |> fun value -> tryParseBool value defaultValidateJwt

            let! validatedPrincipalResult =
                if validateJwt then
                    task {
                        let! validationResult = validateIapJwtAsync context configuration

                        return
                            match validationResult with
                            | Ok principal -> Ok(Some principal)
                            | Error error -> Error error
                    }
                else
                    Task.FromResult(Ok None)

            match validatedPrincipalResult with
            | Error(MissingJwtHeader headerName) ->
                Tracing.addAttribute currentActivity "iap.auth.error" "missing_jwt_assertion_header"
                Tracing.addAttribute currentActivity "iap.auth.jwt_header" headerName
                Tracing.setSpanStatus currentActivity false (Some "Missing IAP JWT assertion header")

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status401Unauthorized
                        "missing_iap_jwt_assertion"
                        "Unauthorized"
                        $"Missing or invalid {headerName} header."
                        [ "header", headerName ]
            | Error(Misconfigured errorMessage) ->
                Tracing.addAttribute currentActivity "iap.auth.error" "jwt_validation_misconfigured"
                Tracing.setSpanStatus currentActivity false (Some "IAP JWT validation is misconfigured")
                logger.LogError("IAP JWT validation is misconfigured: {Error}", errorMessage)

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status500InternalServerError
                        "iap_jwt_validation_misconfigured"
                        "Internal Server Error"
                        "IAP JWT validation is misconfigured."
                        []
            | Error(KeyFetchFailed errorMessage) ->
                Tracing.addAttribute currentActivity "iap.auth.error" "jwt_key_fetch_failed"
                Tracing.setSpanStatus currentActivity false (Some "Failed to fetch IAP signing keys")
                logger.LogError("Failed to fetch IAP signing keys: {Error}", errorMessage)

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status500InternalServerError
                        "iap_signing_keys_unavailable"
                        "Internal Server Error"
                        "Failed to validate the IAP JWT assertion."
                        []
            | Error(InvalidToken errorMessage) ->
                Tracing.addAttribute currentActivity "iap.auth.error" "invalid_jwt_assertion"
                Tracing.setSpanStatus currentActivity false (Some "Invalid IAP JWT assertion")
                logger.LogWarning("IAP JWT assertion validation failed: {Error}", errorMessage)

                do!
                    ProblemResponses.write
                        context
                        StatusCodes.Status401Unauthorized
                        "invalid_iap_jwt_assertion"
                        "Unauthorized"
                        "Invalid IAP JWT assertion."
                        []
            | Ok validatedPrincipalOption ->
                let validatedPrincipal =
                    match validatedPrincipalOption with
                    | Some principal -> Some principal
                    | None -> None

                let emailHeader =
                    configuration[ConfigurationKeys.Auth.IAP.EmailHeader]
                    |> Option.ofObj
                    |> Option.defaultValue defaultEmailHeader

                let nameHeader =
                    configuration[ConfigurationKeys.Auth.IAP.NameHeader]
                    |> Option.ofObj
                    |> Option.defaultValue defaultNameHeader

                let pictureHeader =
                    configuration[ConfigurationKeys.Auth.IAP.PictureHeader]
                    |> Option.ofObj
                    |> Option.defaultValue defaultPictureHeader

                let groupsHeader =
                    configuration[ConfigurationKeys.Auth.IAP.GroupsHeader]
                    |> Option.ofObj
                    |> Option.defaultValue defaultGroupsHeader

                let groupsDelimiter =
                    configuration[ConfigurationKeys.Auth.IAP.GroupsDelimiter]
                    |> Option.ofObj
                    |> Option.defaultValue defaultGroupsDelimiter

                let headerEmail = extractHeader emailHeader context |> Option.map parseEmailValue

                let jwtEmail =
                    validatedPrincipal
                    |> Option.bind findEmailClaim
                    |> Option.map (fun value -> value.Trim().ToLowerInvariant())

                let resolvedEmail =
                    match validatedPrincipal, jwtEmail with
                    | Some _, Some tokenEmail ->
                        match headerEmail with
                        | Some currentHeaderEmail when
                            not (tokenEmail.Equals(currentHeaderEmail, StringComparison.OrdinalIgnoreCase))
                            ->
                            Tracing.addAttribute currentActivity "iap.auth.warning" "jwt_email_header_mismatch"

                            logger.LogWarning(
                                "IAP JWT email claim mismatch. Header email: {HeaderEmail}, token email: {TokenEmail}",
                                currentHeaderEmail,
                                tokenEmail
                            )
                        | _ -> ()

                        Some tokenEmail
                    | Some _, None ->
                        Tracing.addAttribute currentActivity "iap.auth.error" "missing_jwt_email_claim"
                        Tracing.setSpanStatus currentActivity false (Some "IAP JWT assertion missing email claim")
                        logger.LogWarning("Validated IAP JWT assertion did not include an email claim.")
                        None
                    | None, _ -> headerEmail

                match validatedPrincipal, resolvedEmail with
                | Some _, None ->
                    do!
                        ProblemResponses.write
                            context
                            StatusCodes.Status401Unauthorized
                            "invalid_iap_jwt_assertion"
                            "Unauthorized"
                            "Invalid IAP JWT assertion."
                            []
                | None, None ->
                    Tracing.addAttribute currentActivity "iap.auth.error" "missing_email_header"
                    Tracing.addAttribute currentActivity "iap.auth.header" emailHeader
                    Tracing.setSpanStatus currentActivity false (Some "Missing IAP email header")

                    do!
                        ProblemResponses.write
                            context
                            StatusCodes.Status401Unauthorized
                            "missing_iap_email_header"
                            "Unauthorized"
                            $"Missing or invalid {emailHeader} header."
                            [ "header", emailHeader ]
                | _, Some userEmail ->
                    let userName = extractHeader nameHeader context
                    let profilePicUrl = extractHeader pictureHeader context
                    let iapGroupKeys = parseGroups (extractHeader groupsHeader context) groupsDelimiter

                    let directoryIdentityService =
                        context.RequestServices.GetRequiredService<IGoogleDirectoryIdentityService>()

                    let! directoryIdentityData = directoryIdentityService.GetIdentityDataAsync(userEmail)
                    let directoryGroupKeys = directoryIdentityData.GroupKeys

                    let resolvedProfilePicUrl =
                        directoryIdentityData.ProfilePicUrl |> Option.orElse profilePicUrl

                    let groupKeys = (iapGroupKeys @ directoryGroupKeys) |> List.distinct

                    let provisioningService =
                        context.RequestServices.GetRequiredService<IIdentityProvisioningService>()

                    let! result =
                        provisioningService.EnsureUserAsync {
                            Email = userEmail
                            Name = userName
                            ProfilePicUrl = resolvedProfilePicUrl
                            GroupKeys = groupKeys
                            Source = "iap"
                        }

                    match result with
                    | Error(InvalidEmailFormat errorMessage) ->
                        Tracing.addAttribute currentActivity "iap.auth.error" "invalid_email_format"
                        Tracing.addAttribute currentActivity "iap.auth.user_email" userEmail

                        Tracing.setSpanStatus currentActivity false (Some "Invalid email format in IAP identity")

                        logger.LogWarning("Failed to provision IAP user {Email}: {Error}", userEmail, errorMessage)

                        do!
                            ProblemResponses.write
                                context
                                StatusCodes.Status401Unauthorized
                                "invalid_iap_email"
                                "Unauthorized"
                                "Invalid authenticated user email."
                                []
                    | Error error ->
                        let errorMessage = IdentityProvisioningError.toMessage error
                        Tracing.addAttribute currentActivity "iap.auth.error" "provisioning_failed"
                        Tracing.addAttribute currentActivity "iap.auth.user_email" userEmail
                        Tracing.setSpanStatus currentActivity false (Some "Failed to provision user")
                        logger.LogWarning("Failed to provision IAP user {Email}: {Error}", userEmail, errorMessage)

                        do!
                            ProblemResponses.write
                                context
                                StatusCodes.Status500InternalServerError
                                "iap_identity_provisioning_failed"
                                "Internal Server Error"
                                "Failed to provision the authenticated user."
                                []
                    | Ok userId ->
                        context.Items.["UserId"] <- userId

                        RequestUserContext.set context {
                            UserId = Some userId
                            Name = userName
                            Email = userEmail
                            Profile = resolvedProfilePicUrl
                            GroupKeys = groupKeys
                            AuthenticationSource = "iap"
                        }

                        Tracing.addAttribute currentActivity "iap.auth.user_email" userEmail
                        Tracing.addAttribute currentActivity "iap.auth.groups_count" (string groupKeys.Length)

                        Tracing.addAttribute currentActivity "iap.auth.iap_groups_count" (string iapGroupKeys.Length)

                        Tracing.addAttribute
                            currentActivity
                            "iap.auth.directory_groups_count"
                            (string directoryGroupKeys.Length)

                        Tracing.addAttribute currentActivity "user.id" (userId.Value.ToString())
                        Tracing.addAttribute currentActivity "iap.auth.success" "true"
                        Tracing.setSpanStatus currentActivity true None
                        do! next.Invoke context
    }