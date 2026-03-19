namespace Freetool.Api.Services

open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces

module DevSeedingService =

    /// Ensures OpenFGA relationships exist for dev mode users and spaces.
    /// This is called after the authorization model is written to handle the case
    /// where the OpenFGA store was recreated but the database still has users.
    /// All relationship creation is idempotent - if the relationships already exist, this succeeds.
    let ensureOpenFgaRelationshipsAsync
        (logger: ILogger)
        (userRepository: IUserRepository)
        (spaceRepository: ISpaceRepository)
        (authService: IAuthorizationService)
        : Task<unit> =
        task {
            logger.LogInformation("[DEV MODE] Ensuring OpenFGA relationships exist...")

            // Parse emails for lookup
            let adminEmailResult = Email.Create(Some "admin@test.local")
            let moderatorEmailResult = Email.Create(Some "moderator@test.local")
            let memberEmailResult = Email.Create(Some "member@test.local")
            let nopermEmailResult = Email.Create(Some "noperm@test.local")

            match adminEmailResult, moderatorEmailResult, memberEmailResult, nopermEmailResult with
            | Ok adminEmail, Ok moderatorEmail, Ok memberEmail, Ok nopermEmail ->
                // Look up users by email
                let! adminUserOpt = userRepository.GetByEmailAsync adminEmail
                let! moderatorUserOpt = userRepository.GetByEmailAsync moderatorEmail
                let! memberUserOpt = userRepository.GetByEmailAsync memberEmail
                let! nopermUserOpt = userRepository.GetByEmailAsync nopermEmail

                match adminUserOpt with
                | None -> logger.LogInformation("[DEV MODE] Admin user not found, skipping relationship seeding")
                | Some adminUser ->
                    let adminUserId = adminUser.State.Id
                    let adminUserIdStr = adminUserId.Value.ToString()

                    // 1. Ensure org admin relationship
                    try
                        do! authService.InitializeOrganizationAsync "default" adminUserIdStr

                        logger.LogInformation(
                            "[DEV MODE] Ensured org admin relationship for user {UserId}",
                            adminUserIdStr
                        )
                    with ex ->
                        logger.LogWarning("[DEV MODE] Failed to ensure org admin relationship: {Error}", ex.Message)

                    // 2. Look up the test space by name
                    let! spaces = spaceRepository.GetAllAsync 0 100

                    let testSpaceOpt = spaces |> List.tryFind (fun s -> s.State.Name = "Test Space")

                    match testSpaceOpt with
                    | None ->
                        logger.LogInformation("[DEV MODE] Test Space not found, skipping space relationship seeding")
                    | Some space ->
                        let spaceId = Space.getId space
                        let spaceIdStr = spaceId.Value.ToString()

                        // 3. Ensure organization relation for the space
                        try
                            let orgTuple = {
                                Subject = Organization "default"
                                Relation = SpaceOrganization
                                Object = SpaceObject spaceIdStr
                            }

                            do! authService.CreateRelationshipsAsync [ orgTuple ]

                            logger.LogInformation(
                                "[DEV MODE] Ensured organization relation for space {SpaceId}",
                                spaceIdStr
                            )
                        with ex ->
                            logger.LogWarning("[DEV MODE] Failed to ensure org relation for space: {Error}", ex.Message)

                        // 4. Ensure moderator relation
                        match moderatorUserOpt with
                        | None ->
                            logger.LogInformation("[DEV MODE] Moderator user not found, skipping moderator relation")
                        | Some moderatorUser ->
                            let moderatorUserId = moderatorUser.State.Id
                            let moderatorUserIdStr = moderatorUserId.Value.ToString()

                            try
                                let moderatorTuple = {
                                    Subject = User moderatorUserIdStr
                                    Relation = SpaceModerator
                                    Object = SpaceObject spaceIdStr
                                }

                                do! authService.CreateRelationshipsAsync [ moderatorTuple ]

                                logger.LogInformation(
                                    "[DEV MODE] Ensured moderator relation for user {UserId}",
                                    moderatorUserIdStr
                                )
                            with ex ->
                                logger.LogWarning("[DEV MODE] Failed to ensure moderator relation: {Error}", ex.Message)

                        // 5. Ensure member relations
                        let memberTuples = ResizeArray<RelationshipTuple>()

                        match memberUserOpt with
                        | Some memberUser ->
                            let memberUserId = memberUser.State.Id
                            let memberUserIdStr = memberUserId.Value.ToString()

                            memberTuples.Add(
                                {
                                    Subject = User memberUserIdStr
                                    Relation = SpaceMember
                                    Object = SpaceObject spaceIdStr
                                }
                            )

                            // Also add run_app permission for member
                            try
                                let runAppTuple = {
                                    Subject = User memberUserIdStr
                                    Relation = AppRun
                                    Object = SpaceObject spaceIdStr
                                }

                                do! authService.CreateRelationshipsAsync [ runAppTuple ]

                                logger.LogInformation(
                                    "[DEV MODE] Ensured run_app permission for member user {UserId}",
                                    memberUserIdStr
                                )
                            with ex ->
                                logger.LogWarning("[DEV MODE] Failed to ensure run_app permission: {Error}", ex.Message)
                        | None -> logger.LogInformation("[DEV MODE] Member user not found")

                        match nopermUserOpt with
                        | Some nopermUser ->
                            let nopermUserId = nopermUser.State.Id
                            let nopermUserIdStr = nopermUserId.Value.ToString()

                            memberTuples.Add(
                                {
                                    Subject = User nopermUserIdStr
                                    Relation = SpaceMember
                                    Object = SpaceObject spaceIdStr
                                }
                            )
                        | None -> logger.LogInformation("[DEV MODE] Noperm user not found")

                        if memberTuples.Count > 0 then
                            try
                                do! authService.CreateRelationshipsAsync(memberTuples |> Seq.toList)
                                logger.LogInformation("[DEV MODE] Ensured member relations")
                            with ex ->
                                logger.LogWarning("[DEV MODE] Failed to ensure member relations: {Error}", ex.Message)

                        logger.LogInformation("[DEV MODE] OpenFGA relationship seeding complete!")
            | _ -> logger.LogWarning("[DEV MODE] Failed to parse dev user emails")
        }

    /// Seeds the dev database with test users, a space, resource, folder, and app
    /// Only runs when the database is empty (no users exist)
    let seedDataAsync
        (logger: ILogger)
        (userRepository: IUserRepository)
        (spaceRepository: ISpaceRepository)
        (resourceRepository: IResourceRepository)
        (folderRepository: IFolderRepository)
        (appRepository: IAppRepository)
        (authService: IAuthorizationService)
        : Task<unit> =
        task {
            // Check if database already has users - if so, skip seeding
            let! userCount = userRepository.GetCountAsync()

            if userCount > 0 then
                logger.LogInformation("[DEV MODE] Database already has users, skipping seed data")
                return ()
            else
                logger.LogInformation("[DEV MODE] Seeding dev database with test data...")

                // Create test users
                // 1. Admin user - will become org admin
                let adminEmail =
                    match Email.Create(Some "admin@test.local") with
                    | Ok e -> e
                    | Error _ -> failwith "Invalid admin email"

                let adminUser = User.create "Org Admin" adminEmail None

                match! userRepository.AddAsync adminUser with
                | Error err -> logger.LogWarning("[DEV MODE] Failed to create admin user: {Error}", err)
                | Ok() ->
                    let adminUserId = adminUser.State.Id
                    logger.LogInformation("[DEV MODE] Created admin user: {UserId}", adminUserId.Value.ToString())

                    // Set admin as org admin
                    try
                        do! authService.InitializeOrganizationAsync "default" (adminUserId.Value.ToString())
                        logger.LogInformation("[DEV MODE] Set admin user as organization admin")
                    with ex ->
                        logger.LogWarning("[DEV MODE] Failed to set org admin: {Error}", ex.Message)

                    // 2. Moderator user
                    let moderatorEmail =
                        match Email.Create(Some "moderator@test.local") with
                        | Ok e -> e
                        | Error _ -> failwith "Invalid moderator email"

                    let moderatorUser = User.create "Space Moderator" moderatorEmail None

                    match! userRepository.AddAsync moderatorUser with
                    | Error err -> logger.LogWarning("[DEV MODE] Failed to create moderator user: {Error}", err)
                    | Ok() ->
                        let moderatorUserId = moderatorUser.State.Id

                        logger.LogInformation(
                            "[DEV MODE] Created moderator user: {UserId}",
                            moderatorUserId.Value.ToString()
                        )

                        // 3. Member user
                        let memberEmail =
                            match Email.Create(Some "member@test.local") with
                            | Ok e -> e
                            | Error _ -> failwith "Invalid member email"

                        let memberUser = User.create "Regular Member" memberEmail None

                        match! userRepository.AddAsync memberUser with
                        | Error err -> logger.LogWarning("[DEV MODE] Failed to create member user: {Error}", err)
                        | Ok() ->
                            let memberUserId = memberUser.State.Id

                            logger.LogInformation(
                                "[DEV MODE] Created member user: {UserId}",
                                memberUserId.Value.ToString()
                            )

                            // 4. No permissions user
                            let nopermEmail =
                                match Email.Create(Some "noperm@test.local") with
                                | Ok e -> e
                                | Error _ -> failwith "Invalid noperm email"

                            let nopermUser = User.create "No Permissions" nopermEmail None

                            match! userRepository.AddAsync nopermUser with
                            | Error err -> logger.LogWarning("[DEV MODE] Failed to create noperm user: {Error}", err)
                            | Ok() ->
                                let nopermUserId = nopermUser.State.Id

                                logger.LogInformation(
                                    "[DEV MODE] Created noperm user: {UserId}",
                                    nopermUserId.Value.ToString()
                                )

                                // 5. Not a member user - exists but is not a member of any space
                                let notamemberEmail =
                                    match Email.Create(Some "notamember@test.local") with
                                    | Ok e -> e
                                    | Error _ -> failwith "Invalid notamember email"

                                let notamemberUser = User.create "Not a Member" notamemberEmail None

                                match! userRepository.AddAsync notamemberUser with
                                | Error err ->
                                    logger.LogWarning("[DEV MODE] Failed to create notamember user: {Error}", err)
                                | Ok() ->
                                    let notamemberUserId = notamemberUser.State.Id

                                    logger.LogInformation(
                                        "[DEV MODE] Created notamember user: {UserId}",
                                        notamemberUserId.Value.ToString()
                                    )

                                    // Suppress unused variable warning - this user intentionally has no space membership
                                    ignore notamemberUserId

                                // Create test space with moderator and members
                                match
                                    Space.create
                                        adminUserId
                                        "Test Space"
                                        moderatorUserId
                                        (Some [ memberUserId; nopermUserId ])
                                with
                                | Error err -> logger.LogWarning("[DEV MODE] Failed to create space: {Error}", err)
                                | Ok space ->
                                    match! spaceRepository.AddAsync space with
                                    | Error err -> logger.LogWarning("[DEV MODE] Failed to save space: {Error}", err)
                                    | Ok() ->
                                        let spaceId = Space.getId space
                                        let spaceIdStr = spaceId.Value.ToString()
                                        logger.LogInformation("[DEV MODE] Created Test Space: {SpaceId}", spaceIdStr)

                                        // Set up organization relation for the space
                                        try
                                            let orgTuple = {
                                                Subject = Organization "default"
                                                Relation = SpaceOrganization
                                                Object = SpaceObject spaceIdStr
                                            }

                                            do! authService.CreateRelationshipsAsync [ orgTuple ]
                                            logger.LogInformation("[DEV MODE] Set up organization relation for space")
                                        with ex ->
                                            logger.LogWarning(
                                                "[DEV MODE] Failed to set org relation for space: {Error}",
                                                ex.Message
                                            )

                                        // Set up moderator relation in OpenFGA
                                        try
                                            let moderatorTuple = {
                                                Subject = User(moderatorUserId.Value.ToString())
                                                Relation = SpaceModerator
                                                Object = SpaceObject spaceIdStr
                                            }

                                            do! authService.CreateRelationshipsAsync [ moderatorTuple ]
                                            logger.LogInformation("[DEV MODE] Set up moderator relation")
                                        with ex ->
                                            logger.LogWarning(
                                                "[DEV MODE] Failed to set moderator relation: {Error}",
                                                ex.Message
                                            )

                                        // Set up member relations in OpenFGA
                                        try
                                            let memberTuples = [
                                                {
                                                    Subject = User(memberUserId.Value.ToString())
                                                    Relation = SpaceMember
                                                    Object = SpaceObject spaceIdStr
                                                }
                                                {
                                                    Subject = User(nopermUserId.Value.ToString())
                                                    Relation = SpaceMember
                                                    Object = SpaceObject spaceIdStr
                                                }
                                            ]

                                            do! authService.CreateRelationshipsAsync memberTuples
                                            logger.LogInformation("[DEV MODE] Set up member relations")
                                        with ex ->
                                            logger.LogWarning(
                                                "[DEV MODE] Failed to set member relations: {Error}",
                                                ex.Message
                                            )

                                        // Give member user run_app permission
                                        try
                                            let runAppTuple = {
                                                Subject = User(memberUserId.Value.ToString())
                                                Relation = AppRun
                                                Object = SpaceObject spaceIdStr
                                            }

                                            do! authService.CreateRelationshipsAsync [ runAppTuple ]
                                            logger.LogInformation("[DEV MODE] Set up run_app permission for member")
                                        with ex ->
                                            logger.LogWarning(
                                                "[DEV MODE] Failed to set run_app permission: {Error}",
                                                ex.Message
                                            )

                                        // Create a resource in the space
                                        match
                                            Resource.create
                                                adminUserId
                                                spaceId
                                                "Sample API"
                                                "A sample API resource for testing"
                                                "https://httpbin.org/get"
                                                [] [] []
                                        with
                                        | Error err ->
                                            logger.LogWarning("[DEV MODE] Failed to create resource: {Error}", err)
                                        | Ok resource ->
                                            match! resourceRepository.AddAsync resource with
                                            | Error err ->
                                                logger.LogWarning("[DEV MODE] Failed to save resource: {Error}", err)
                                            | Ok() ->
                                                let resourceId = Resource.getId resource

                                                logger.LogInformation(
                                                    "[DEV MODE] Created Sample API resource: {ResourceId}",
                                                    resourceId.Value.ToString()
                                                )

                                                // Create a folder in the space
                                                match Folder.create adminUserId "Sample Folder" None spaceId with
                                                | Error err ->
                                                    logger.LogWarning(
                                                        "[DEV MODE] Failed to create folder: {Error}",
                                                        err
                                                    )
                                                | Ok folder ->
                                                    match! folderRepository.AddAsync folder with
                                                    | Error err ->
                                                        logger.LogWarning(
                                                            "[DEV MODE] Failed to save folder: {Error}",
                                                            err
                                                        )
                                                    | Ok() ->
                                                        let folderId = Folder.getId folder

                                                        logger.LogInformation(
                                                            "[DEV MODE] Created Sample Folder: {FolderId}",
                                                            folderId.Value.ToString()
                                                        )

                                                        // Create an app in the folder
                                                        match
                                                            App.create
                                                                adminUserId
                                                                "Hello World"
                                                                folderId
                                                                resource
                                                                HttpMethod.Get
                                                                []
                                                                None
                                                                []
                                                                []
                                                                []
                                                                false
                                                                None
                                                        with
                                                        | Error err ->
                                                            logger.LogWarning(
                                                                "[DEV MODE] Failed to create app: {Error}",
                                                                err
                                                            )
                                                        | Ok app ->
                                                            match! appRepository.AddAsync app with
                                                            | Error err ->
                                                                logger.LogWarning(
                                                                    "[DEV MODE] Failed to save app: {Error}",
                                                                    err
                                                                )
                                                            | Ok() ->
                                                                let appId = App.getId app

                                                                logger.LogInformation(
                                                                    "[DEV MODE] Created Hello World app: {AppId}",
                                                                    appId.Value.ToString()
                                                                )

                                                                logger.LogInformation(
                                                                    "[DEV MODE] Dev database seeding complete!"
                                                                )

                return ()
        }