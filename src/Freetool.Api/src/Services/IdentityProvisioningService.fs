namespace Freetool.Api.Services

open System
open System.Threading.Tasks
open System.Security.Cryptography
open System.Text
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.Interfaces
open Freetool.Api

type IdentityProvisioningContext = {
    Email: string
    Name: string option
    ProfilePicUrl: string option
    GroupKeys: string list
    Source: string
}

type IdentityProvisioningError =
    | InvalidEmailFormat of string
    | CreateUserFailed of string
    | ActivateUserFailed of string
    | SaveActivatedUserFailed of string
    | ProvisioningFailed of string

module IdentityProvisioningError =
    let toMessage (error: IdentityProvisioningError) =
        match error with
        | InvalidEmailFormat message -> message
        | CreateUserFailed message -> message
        | ActivateUserFailed message -> message
        | SaveActivatedUserFailed message -> message
        | ProvisioningFailed message -> message

type IIdentityProvisioningService =
    abstract member EnsureUserAsync: IdentityProvisioningContext -> Task<Result<UserId, IdentityProvisioningError>>

type IdentityProvisioningService
    (
        userRepository: IUserRepository,
        authService: IAuthorizationService,
        mappingRepository: IIdentityGroupSpaceMappingRepository,
        spaceRepository: ISpaceRepository,
        configuration: IConfiguration,
        logger: ILogger<IdentityProvisioningService>
    ) =

    let domainErrorToMessage (err: DomainError) =
        match err with
        | ValidationError msg -> $"Validation error: {msg}"
        | NotFound msg -> $"Not found: {msg}"
        | Conflict msg -> $"Conflict: {msg}"
        | InvalidOperation msg -> $"Invalid operation: {msg}"

    let ensureOrgAdminIfConfigured (email: string) (userId: UserId) = task {
        let orgAdminEmail = configuration[ConfigurationKeys.OpenFGA.OrgAdminEmail]

        if
            not (String.IsNullOrEmpty(orgAdminEmail))
            && email.Equals(orgAdminEmail, StringComparison.OrdinalIgnoreCase)
        then
            try
                do! authService.InitializeOrganizationAsync "default" (userId.Value.ToString())
            with ex ->
                logger.LogWarning("Failed to ensure org admin for {Email}: {Error}", email, ex.Message)
    }

    let normalizeGroupKeys (groupKeys: string list) =
        groupKeys
        |> List.map (fun key -> key.Trim())
        |> List.filter (fun key -> not (String.IsNullOrWhiteSpace key))
        |> List.distinct

    let normalizeDisplayName (name: string option) =
        name
        |> Option.bind (fun value ->
            if String.IsNullOrWhiteSpace value then
                None
            else
                Some(value.Trim()))

    let truncateSpaceName (name: string) (hashSeed: string) =
        if name.Length <= 100 then
            name
        else
            use sha = SHA256.Create()
            let bytes = Encoding.UTF8.GetBytes(hashSeed)

            let hash =
                sha.ComputeHash(bytes)
                |> Array.take 4
                |> Array.map (fun b -> b.ToString("x2"))
                |> String.concat ""

            let maxPrefix = 100 - (hash.Length + 1)
            let prefix = name.Substring(0, maxPrefix).TrimEnd()
            $"{prefix}-{hash}"

    let getOrgUnitSegments (orgUnitPath: string) =
        orgUnitPath.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun segment -> segment.Trim())
        |> Array.filter (fun segment -> not (String.IsNullOrWhiteSpace segment))
        |> Array.toList

    let deriveSpaceNamesFromOrgUnitPath (orgUnitPath: string) =
        let segments = getOrgUnitSegments orgUnitPath

        match segments with
        | [] -> "Default Space", "Default Space"
        | _ ->
            let preferredName = segments |> List.last
            let fullPathName = String.concat "/" segments
            truncateSpaceName preferredName orgUnitPath, truncateSpaceName fullPathName orgUnitPath

    let getCurrentOrgUnitGroupKey (groupKeys: string list) =
        let ouPrefix =
            configuration[ConfigurationKeys.Auth.GoogleDirectory.OrgUnitKeyPrefix]
            |> Option.ofObj
            |> Option.defaultValue "ou"

        let prefix = $"{ouPrefix}:"

        groupKeys
        |> normalizeGroupKeys
        |> List.choose (fun key ->
            if key.StartsWith(prefix, StringComparison.Ordinal) then
                let orgUnitPath = key.Substring(prefix.Length).Trim()

                if String.IsNullOrWhiteSpace orgUnitPath then
                    None
                else
                    Some(key, orgUnitPath)
            else
                None)
        |> List.sortByDescending (fun (_, orgUnitPath) ->
            orgUnitPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length)
        |> List.tryHead
        |> Option.map fst

    let ensureSpaceForCurrentOrgUnitIfNeeded (userId: UserId) (groupKeys: string list) = task {
        match getCurrentOrgUnitGroupKey groupKeys with
        | None -> return ()
        | Some orgUnitGroupKey ->
            let! allMappings = mappingRepository.GetAllAsync()

            let hasActiveMapping =
                allMappings
                |> List.exists (fun mapping ->
                    mapping.IsActive
                    && mapping.GroupKey.Equals(orgUnitGroupKey, StringComparison.Ordinal))

            if not hasActiveMapping then
                let orgUnitPath =
                    orgUnitGroupKey.Split(':', 2, StringSplitOptions.None)
                    |> fun parts -> if parts.Length = 2 then parts.[1] else orgUnitGroupKey

                let preferredSpaceName, _ = deriveSpaceNamesFromOrgUnitPath orgUnitPath

                let! targetSpaceOption = spaceRepository.GetByNameAsync preferredSpaceName

                let! targetSpace =
                    match targetSpaceOption with
                    | Some existingSpace -> Task.FromResult(Some(existingSpace.State.Id, false))
                    | None ->
                        match Space.create userId preferredSpaceName userId None with
                        | Error error ->
                            logger.LogWarning(
                                "Failed to create auto-provisioned space for OU key {OrgUnitGroupKey}: {Error}",
                                orgUnitGroupKey,
                                error
                            )

                            Task.FromResult(None)
                        | Ok newSpace -> task {
                            match! spaceRepository.AddAsync newSpace with
                            | Error error ->
                                logger.LogWarning(
                                    "Failed to save auto-provisioned space for OU key {OrgUnitGroupKey}: {Error}",
                                    orgUnitGroupKey,
                                    error
                                )

                                return None
                            | Ok() -> return Some(newSpace.State.Id, true)
                          }

                match targetSpace with
                | None -> return ()
                | Some(spaceId, wasCreatedNow) ->
                    let spaceIdStr = spaceId.Value.ToString()
                    let userIdStr = userId.Value.ToString()

                    try
                        let tuples =
                            if wasCreatedNow then
                                [
                                    {
                                        Subject = Organization "default"
                                        Relation = SpaceOrganization
                                        Object = SpaceObject spaceIdStr
                                    }
                                    {
                                        Subject = User userIdStr
                                        Relation = SpaceModerator
                                        Object = SpaceObject spaceIdStr
                                    }
                                ]
                            else
                                [
                                    {
                                        Subject = Organization "default"
                                        Relation = SpaceOrganization
                                        Object = SpaceObject spaceIdStr
                                    }
                                ]

                        do! authService.CreateRelationshipsAsync tuples
                    with ex ->
                        logger.LogWarning(
                            "Failed to configure OpenFGA tuples for auto-provisioned space {SpaceId}: {Error}",
                            spaceIdStr,
                            ex.Message
                        )

                    match! mappingRepository.AddAsync userId orgUnitGroupKey spaceId with
                    | Ok _ -> ()
                    | Error(Conflict _) -> ()
                    | Error error ->
                        logger.LogWarning(
                            "Failed to create OU group-space mapping for {GroupKey} -> {SpaceId}: {Error}",
                            orgUnitGroupKey,
                            spaceIdStr,
                            error
                        )
    }

    let reconcileMappedSpaceMemberships (userId: UserId) (groupKeys: string list) = task {
        let ensureSpaceMemberInRepository (spaceId: SpaceId) = task {
            let! spaceOption = spaceRepository.GetByIdAsync spaceId

            match spaceOption with
            | None ->
                logger.LogWarning(
                    "Cannot ensure mapped membership for user {UserId} because space {SpaceId} was not found in repository",
                    userId.Value,
                    spaceId.Value
                )
            | Some space ->
                if
                    space.State.ModeratorUserId = userId
                    || (space.State.MemberIds |> List.contains userId)
                then
                    ()
                else
                    match Space.addMember userId userId space with
                    | Error error ->
                        logger.LogWarning(
                            "Failed to add user {UserId} to mapped space {SpaceId} in repository: {Error}",
                            userId.Value,
                            spaceId.Value,
                            error
                        )
                    | Ok updatedSpace ->
                        match! spaceRepository.UpdateAsync updatedSpace with
                        | Ok() -> ()
                        | Error error ->
                            logger.LogWarning(
                                "Failed to persist mapped membership for user {UserId} in space {SpaceId}: {Error}",
                                userId.Value,
                                spaceId.Value,
                                error
                            )
        }

        let normalizedGroupKeys = normalizeGroupKeys groupKeys
        let! desiredSpaceIds = mappingRepository.GetSpaceIdsByGroupKeysAsync normalizedGroupKeys

        for spaceId in desiredSpaceIds do
            do! ensureSpaceMemberInRepository spaceId

            try
                do!
                    authService.CreateRelationshipsAsync(
                        [
                            {
                                Subject = User(userId.Value.ToString())
                                Relation = SpaceMember
                                Object = SpaceObject(spaceId.Value.ToString())
                            }
                        ]
                    )
            with ex ->
                logger.LogWarning(
                    "Failed to ensure mapped membership for user {UserId} in space {SpaceId}: {Error}",
                    userId.Value,
                    spaceId.Value,
                    ex.Message
                )
    }

    interface IIdentityProvisioningService with
        member _.EnsureUserAsync
            (context: IdentityProvisioningContext)
            : Task<Result<UserId, IdentityProvisioningError>> =
            task {
                match Email.Create(Some context.Email) with
                | Error err -> return Error(InvalidEmailFormat(domainErrorToMessage err))
                | Ok validEmail ->
                    let! existingUser = userRepository.GetByEmailAsync validEmail

                    match existingUser with
                    | None ->
                        let userName =
                            normalizeDisplayName context.Name |> Option.defaultValue context.Email

                        let newUser = User.create userName validEmail context.ProfilePicUrl

                        match! userRepository.AddAsync newUser with
                        | Error err -> return Error(CreateUserFailed(domainErrorToMessage err))
                        | Ok() ->
                            do! ensureOrgAdminIfConfigured context.Email newUser.State.Id
                            do! ensureSpaceForCurrentOrgUnitIfNeeded newUser.State.Id context.GroupKeys
                            do! reconcileMappedSpaceMemberships newUser.State.Id context.GroupKeys
                            return Ok newUser.State.Id

                    | Some user when User.isInvitedPlaceholder user ->
                        let userName =
                            normalizeDisplayName context.Name |> Option.defaultValue context.Email

                        match User.activate userName context.ProfilePicUrl user with
                        | Error err -> return Error(ActivateUserFailed(domainErrorToMessage err))
                        | Ok activatedUser ->
                            match! userRepository.UpdateAsync activatedUser with
                            | Error err -> return Error(SaveActivatedUserFailed(domainErrorToMessage err))
                            | Ok() ->
                                do! ensureOrgAdminIfConfigured context.Email activatedUser.State.Id
                                do! ensureSpaceForCurrentOrgUnitIfNeeded activatedUser.State.Id context.GroupKeys
                                do! reconcileMappedSpaceMemberships activatedUser.State.Id context.GroupKeys
                                return Ok activatedUser.State.Id

                    | Some user ->
                        do! ensureOrgAdminIfConfigured context.Email user.State.Id
                        do! ensureSpaceForCurrentOrgUnitIfNeeded user.State.Id context.GroupKeys
                        do! reconcileMappedSpaceMemberships user.State.Id context.GroupKeys
                        return Ok user.State.Id
            }