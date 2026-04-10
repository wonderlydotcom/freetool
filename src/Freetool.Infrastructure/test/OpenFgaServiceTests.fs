module Freetool.Infrastructure.Tests.OpenFgaServiceTests

open System
open System.Threading.Tasks
open System.Reflection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open OpenFga.Sdk.Model
open Xunit
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Services

// Test configuration - assumes OpenFGA is running on localhost:8090
let openFgaApiUrl = "http://localhost:8090"

// Helper to create a test service without a store ID (for store creation)
let createServiceWithoutStore () =
    OpenFgaService(openFgaApiUrl, NullLogger<OpenFgaService>.Instance) :> IAuthorizationService

// Helper to create a test service with a store ID
let createServiceWithStore storeId =
    OpenFgaService(openFgaApiUrl, NullLogger<OpenFgaService>.Instance, storeId) :> IAuthorizationService

let createConcreteServiceWithStore storeId =
    OpenFgaService(openFgaApiUrl, NullLogger<OpenFgaService>.Instance, storeId)

type CapturingOpenFgaLogger() =
    let mutable warnings: string list = []

    member _.Warnings = warnings

    interface ILogger<OpenFgaService> with
        member _.BeginScope<'State>(_state: 'State) = null
        member _.IsEnabled(_logLevel: LogLevel) = true

        member _.Log<'State>
            (logLevel: LogLevel, _eventId: EventId, state: 'State, exn: exn, formatter: Func<'State, exn, string>)
            =
            let message = formatter.Invoke(state, exn)

            if logLevel = LogLevel.Warning then
                warnings <- message :: warnings

[<Fact>]
let ``CreateStoreAsync creates a new store successfully`` () : Task = task {
    // Arrange
    let service = createServiceWithoutStore ()
    let storeName = $"test-store-{Guid.NewGuid()}"

    // Act
    let! result = service.CreateStoreAsync({ Name = storeName })

    // Assert
    Assert.NotNull(result)
    Assert.NotEmpty(result.Id)
    Assert.Equal(storeName, result.Name)
}

[<Fact>]
let ``WriteAuthorizationModelAsync writes the model successfully`` () : Task = task {
    // Arrange - Create a store first
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-model-store-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id

    // Act
    let! modelResponse = service.WriteAuthorizationModelAsync()

    // Assert
    Assert.NotNull(modelResponse)
    Assert.NotEmpty(modelResponse.AuthorizationModelId)
}

[<Fact>]
let ``CreateRelationshipsAsync creates tuples successfully`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-tuples-store-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    let tuples = [
        {
            Subject = User "alice"
            Relation = SpaceMember
            Object = SpaceObject "engineering"
        }
    ]

    // Act
    do! service.CreateRelationshipsAsync(tuples)

    // Assert - If no exception is thrown, the operation succeeded
    Assert.True(true)
}

[<Fact>]
let ``CreateRelationshipsAsync is idempotent when tuple already exists`` () : Task = task {
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-idempotent-create-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    let tuple = {
        Subject = User "alice"
        Relation = SpaceMember
        Object = SpaceObject "engineering"
    }

    do! service.CreateRelationshipsAsync([ tuple ])
    do! service.CreateRelationshipsAsync([ tuple ])

    let! isMember = service.CheckPermissionAsync (User "alice") SpaceMember (SpaceObject "engineering")
    Assert.True(isMember)
}

[<Fact>]
let ``DeleteRelationshipsAsync removes tuples successfully`` () : Task = task {
    // Arrange - Create store, write model, and add a tuple
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-delete-store-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    let tuple = {
        Subject = User "bob"
        Relation = SpaceMember
        Object = SpaceObject "engineering"
    }

    do! service.CreateRelationshipsAsync([ tuple ])

    // Act
    do! service.DeleteRelationshipsAsync([ tuple ])

    // Assert - If no exception is thrown, the operation succeeded
    Assert.True(true)
}

[<Fact>]
let ``UpdateRelationshipsAsync adds and removes tuples atomically`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-update-store-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // First add Carol as a member
    let memberTuple = {
        Subject = User "carol"
        Relation = SpaceMember
        Object = SpaceObject "engineering"
    }

    do! service.CreateRelationshipsAsync([ memberTuple ])

    let adminTuple = {
        Subject = User "carol"
        Relation = SpaceModerator
        Object = SpaceObject "engineering"
    }

    // Act - Promote Carol from member to admin
    do!
        service.UpdateRelationshipsAsync(
            {
                TuplesToAdd = [ adminTuple ]
                TuplesToRemove = [ memberTuple ]
            }
        )

    // Assert - Verify Carol is now an admin
    let! isAdmin = service.CheckPermissionAsync (User "carol") SpaceModerator (SpaceObject "engineering")
    Assert.True(isAdmin)

    // Verify Carol is no longer just a member
    let! isMember = service.CheckPermissionAsync (User "carol") SpaceMember (SpaceObject "engineering")
    Assert.False(isMember)
}

[<Fact>]
let ``UpdateRelationshipsAsync is idempotent for add-only duplicate tuples`` () : Task = task {
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-idempotent-update-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    let tuple = {
        Subject = User "carol"
        Relation = SpaceMember
        Object = SpaceObject "engineering"
    }

    do! service.CreateRelationshipsAsync([ tuple ])

    do!
        service.UpdateRelationshipsAsync(
            {
                TuplesToAdd = [ tuple ]
                TuplesToRemove = []
            }
        )

    let! isMember = service.CheckPermissionAsync (User "carol") SpaceMember (SpaceObject "engineering")
    Assert.True(isMember)
}

[<Fact>]
let ``ReadRelationshipsAsync returns typed tuples for a space object`` () : Task = task {
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-read-tuples-{Guid.NewGuid()}"
            }
        )

    let concreteService = createConcreteServiceWithStore storeResponse.Id
    let authService = concreteService :> IAuthorizationService
    let relationshipReader = concreteService :> IAuthorizationRelationshipReader

    let! _ = authService.WriteAuthorizationModelAsync()

    let engineeringTuples = [
        {
            Subject = User "alice"
            Relation = SpaceMember
            Object = SpaceObject "engineering"
        }
        {
            Subject = Organization "default"
            Relation = SpaceOrganization
            Object = SpaceObject "engineering"
        }
        {
            Subject = UserSetFromRelation("space", "engineering", "member")
            Relation = AppRun
            Object = SpaceObject "engineering"
        }
    ]

    let supportTuple = {
        Subject = User "bob"
        Relation = SpaceMember
        Object = SpaceObject "support"
    }

    do! authService.CreateRelationshipsAsync(engineeringTuples @ [ supportTuple ])

    let! relationships = relationshipReader.ReadRelationshipsAsync(SpaceObject "engineering")

    Assert.Equal(3, relationships.Length)
    Assert.Contains(engineeringTuples[0], relationships)
    Assert.Contains(engineeringTuples[1], relationships)
    Assert.Contains(engineeringTuples[2], relationships)
    Assert.DoesNotContain(supportTuple, relationships)
}

[<Fact>]
let ``ReadRelationshipsAsync logs and skips unparseable tuples`` () =
    let logger = CapturingOpenFgaLogger()
    let service = OpenFgaService(openFgaApiUrl, logger)

    let generatedType =
        typeof<OpenFgaService>.Assembly.GetTypes()
        |> Array.find (fun t -> t.FullName.Contains("mappedTuples@405"))

    let ctor =
        generatedType.GetConstructors(BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)
        |> Array.exactlyOne

    let ctorArgs =
        ctor.GetParameters()
        |> Array.map (fun parameter ->
            if parameter.ParameterType = typeof<OpenFgaService> then
                box service
            elif parameter.ParameterType = typeof<string> then
                box "space:engineering"
            elif parameter.ParameterType.IsAssignableFrom(logger.GetType()) then
                box logger
            else
                failwithf
                    "Unexpected constructor parameter for %s: %s"
                    generatedType.FullName
                    parameter.ParameterType.FullName)

    let instance = ctor.Invoke(ctorArgs)

    let invokeMethod =
        generatedType.GetMethod("Invoke", BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic)

    let invalidTuple =
        OpenFga.Sdk.Model.Tuple(
            Key = TupleKey(User = "not-a-valid-subject", Relation = "run_app", Object = "space:engineering"),
            Timestamp = DateTime.UtcNow
        )

    let result = invokeMethod.Invoke(instance, [| box invalidTuple |])

    Assert.Null(result)

    Assert.Contains(logger.Warnings, fun warning -> warning.Contains("Ignoring unparseable OpenFGA tuple"))
    Assert.Contains(logger.Warnings, fun warning -> warning.Contains("space:engineering"))

[<Fact>]
let ``CheckPermissionAsync returns true for granted permission`` () : Task = task {
    // Arrange - Create store, write model, and add permission
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-check-granted-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    let tuple = {
        Subject = User "dave"
        Relation = SpaceMember
        Object = SpaceObject "engineering"
    }

    do! service.CreateRelationshipsAsync([ tuple ])

    // Act
    let! hasPermission = service.CheckPermissionAsync (User "dave") SpaceMember (SpaceObject "engineering")

    // Assert
    Assert.True(hasPermission)
}

[<Fact>]
let ``CheckPermissionAsync returns false for denied permission`` () : Task = task {
    // Arrange - Create store and write model (no tuples)
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-check-denied-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Act
    let! hasPermission = service.CheckPermissionAsync (User "eve") SpaceModerator (SpaceObject "engineering")

    // Assert
    Assert.False(hasPermission)
}

[<Fact>]
let ``Team admin has all workspace permissions`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-admin-perms-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Frank a space moderator of 'main'
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "frank"
                    Relation = SpaceModerator
                    Object = SpaceObject "main"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "main"
                }
            ]
        )

    // Act & Assert - Verify Frank has all 7 permissions (as moderator of 'main')
    let! canCreateResource = service.CheckPermissionAsync (User "frank") ResourceCreate (SpaceObject "main")
    Assert.True(canCreateResource, "Team admin should have create_resource permission")

    let! canEditResource = service.CheckPermissionAsync (User "frank") ResourceEdit (SpaceObject "main")
    Assert.True(canEditResource, "Team admin should have edit_resource permission")

    let! canDeleteResource = service.CheckPermissionAsync (User "frank") ResourceDelete (SpaceObject "main")
    Assert.True(canDeleteResource, "Team admin should have delete_resource permission")

    let! canCreateApp = service.CheckPermissionAsync (User "frank") AppCreate (SpaceObject "main")
    Assert.True(canCreateApp, "Team admin should have create_app permission")

    let! canEditApp = service.CheckPermissionAsync (User "frank") AppEdit (SpaceObject "main")
    Assert.True(canEditApp, "Team admin should have edit_app permission")

    let! canDeleteApp = service.CheckPermissionAsync (User "frank") AppDelete (SpaceObject "main")
    Assert.True(canDeleteApp, "Team admin should have delete_app permission")

    let! canRunApp = service.CheckPermissionAsync (User "frank") AppRun (SpaceObject "main")
    Assert.True(canRunApp, "Team admin should have run_app permission")
}

[<Fact>]
let ``Global admin has all workspace permissions`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-global-admin-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Grace a global admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "grace"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "sales-dashboard"
                }
            ]
        )

    // Grant global admins all permissions on the workspace
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = ResourceCreate
                    Object = SpaceObject "sales-dashboard"
                }
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = AppRun
                    Object = SpaceObject "sales-dashboard"
                }
            ]
        )

    // Act & Assert - Verify Grace has all permissions on any workspace
    let! canCreateResource = service.CheckPermissionAsync (User "grace") ResourceCreate (SpaceObject "sales-dashboard")

    Assert.True(canCreateResource, "Global admin should have create_resource permission")

    let! canRunApp = service.CheckPermissionAsync (User "grace") AppRun (SpaceObject "sales-dashboard")
    Assert.True(canRunApp, "Global admin should have run_app permission")
}

[<Fact>]
let ``Team member with granted permission can access resource`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-member-perm-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Henry a team member
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "henry"
                    Relation = SpaceMember
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Grant Henry specific permission on workspace
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "henry"
                    Relation = AppRun
                    Object = SpaceObject "main"
                }
            ]
        )

    // Act
    let! canRunApp = service.CheckPermissionAsync (User "henry") AppRun (SpaceObject "main")
    let! canEditApp = service.CheckPermissionAsync (User "henry") AppEdit (SpaceObject "main")

    // Assert
    Assert.True(canRunApp, "User should have explicitly granted run_app permission")
    Assert.False(canEditApp, "User should NOT have edit_app permission (not granted)")
}

[<Fact>]
let ``Only organization admin can create workspaces`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-workspace-create-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Alice an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "alice"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "new-workspace"
                }
            ]
        )

    // Act & Assert - Verify Alice can create workspaces
    let! canCreate = service.CheckPermissionAsync (User "alice") SpaceCreate (SpaceObject "new-workspace")
    Assert.True(canCreate, "Organization admin should be able to create workspaces")
}

[<Fact>]
let ``Team admin cannot create workspaces`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-no-create-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Bob a team admin (NOT an org admin)
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "bob"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "new-workspace"
                }
            ]
        )

    // Act & Assert - Verify Bob CANNOT create workspaces
    let! canCreate = service.CheckPermissionAsync (User "bob") SpaceCreate (SpaceObject "new-workspace")
    Assert.False(canCreate, "Team admin should NOT be able to create workspaces (only org admin can)")
}

[<Fact>]
let ``Only organization admin can rename teams`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-rename-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Carol an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "carol"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Associate team with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify Carol can rename teams
    let! canRename = service.CheckPermissionAsync (User "carol") SpaceRename (SpaceObject "engineering")
    Assert.True(canRename, "Organization admin should be able to rename teams")
}

[<Fact>]
let ``Space moderator can rename their space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-moderator-rename-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Dave a space moderator
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "dave"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Associate space with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify Dave CAN rename space (moderators have rename permission)
    let! canRename = service.CheckPermissionAsync (User "dave") SpaceRename (SpaceObject "engineering")
    Assert.True(canRename, "Space moderator should be able to rename their space")
}

[<Fact>]
let ``Only organization admin can delete teams`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-delete-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Eve an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "eve"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Associate team with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify Eve can delete teams
    let! canDelete = service.CheckPermissionAsync (User "eve") SpaceDelete (SpaceObject "engineering")
    Assert.True(canDelete, "Organization admin should be able to delete teams")
}

[<Fact>]
let ``Team admin cannot delete teams`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-no-delete-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Frank a team admin (NOT an org admin)
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "frank"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Associate team with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify Frank CANNOT delete teams
    let! canDelete = service.CheckPermissionAsync (User "frank") SpaceDelete (SpaceObject "engineering")
    Assert.False(canDelete, "Team admin should NOT be able to delete teams (only org admin can)")
}

[<Fact>]
let ``Team admin has all folder permissions`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-folder-perms-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make George a space moderator of 'main'
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "george"
                    Relation = SpaceModerator
                    Object = SpaceObject "main"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "main"
                }
            ]
        )

    // Act & Assert - Verify George has all 3 folder permissions (as moderator of 'main')
    let! canCreateFolder = service.CheckPermissionAsync (User "george") FolderCreate (SpaceObject "main")
    Assert.True(canCreateFolder, "Team admin should have create_folder permission")

    let! canEditFolder = service.CheckPermissionAsync (User "george") FolderEdit (SpaceObject "main")
    Assert.True(canEditFolder, "Team admin should have edit_folder permission")

    let! canDeleteFolder = service.CheckPermissionAsync (User "george") FolderDelete (SpaceObject "main")
    Assert.True(canDeleteFolder, "Team admin should have delete_folder permission")
}

[<Fact>]
let ``Global admin has all folder permissions`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-global-folder-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Hannah a global admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "hannah"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Associate workspace with organization
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "main"
                }
            ]
        )

    // Grant global admins folder permissions on the workspace
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = FolderCreate
                    Object = SpaceObject "main"
                }
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = FolderEdit
                    Object = SpaceObject "main"
                }
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = FolderDelete
                    Object = SpaceObject "main"
                }
            ]
        )

    // Act & Assert - Verify Hannah has all folder permissions
    let! canCreateFolder = service.CheckPermissionAsync (User "hannah") FolderCreate (SpaceObject "main")
    Assert.True(canCreateFolder, "Global admin should have create_folder permission")

    let! canEditFolder = service.CheckPermissionAsync (User "hannah") FolderEdit (SpaceObject "main")
    Assert.True(canEditFolder, "Global admin should have edit_folder permission")

    let! canDeleteFolder = service.CheckPermissionAsync (User "hannah") FolderDelete (SpaceObject "main")
    Assert.True(canDeleteFolder, "Global admin should have delete_folder permission")
}

[<Fact>]
let ``Team member with granted folder permission can manage folders`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-member-folder-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Ivan a team member
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "ivan"
                    Relation = SpaceMember
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Grant Ivan specific folder permissions on workspace
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "ivan"
                    Relation = FolderCreate
                    Object = SpaceObject "main"
                }
                {
                    Subject = User "ivan"
                    Relation = FolderEdit
                    Object = SpaceObject "main"
                }
            ]
        )

    // Act & Assert
    let! canCreateFolder = service.CheckPermissionAsync (User "ivan") FolderCreate (SpaceObject "main")
    Assert.True(canCreateFolder, "User should have explicitly granted create_folder permission")

    let! canEditFolder = service.CheckPermissionAsync (User "ivan") FolderEdit (SpaceObject "main")
    Assert.True(canEditFolder, "User should have explicitly granted edit_folder permission")

    let! canDeleteFolder = service.CheckPermissionAsync (User "ivan") FolderDelete (SpaceObject "main")
    Assert.False(canDeleteFolder, "User should NOT have delete_folder permission (not granted)")
}

[<Fact>]
let ``Org admin inherits workspace permissions via tupleToUserset`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-org-inherit-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // 1. Make Alice an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "alice"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // 2. Associate workspace with organization (NOT team, NOT direct grants)
    // This is the pattern used by commit 5be26020 to enable org admin inheritance
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "main"
                }
            ]
        )

    // 3. Check permissions - should inherit via tupleToUserset (NO direct grants created)
    // If this fails, the OpenFGA model's tupleToUserset for org admin is broken
    let! canCreateResource = service.CheckPermissionAsync (User "alice") ResourceCreate (SpaceObject "main")
    Assert.True(canCreateResource, "Org admin should inherit create_resource via tupleToUserset")

    let! canEditResource = service.CheckPermissionAsync (User "alice") ResourceEdit (SpaceObject "main")
    Assert.True(canEditResource, "Org admin should inherit edit_resource via tupleToUserset")

    let! canDeleteResource = service.CheckPermissionAsync (User "alice") ResourceDelete (SpaceObject "main")
    Assert.True(canDeleteResource, "Org admin should inherit delete_resource via tupleToUserset")

    let! canCreateApp = service.CheckPermissionAsync (User "alice") AppCreate (SpaceObject "main")
    Assert.True(canCreateApp, "Org admin should inherit create_app via tupleToUserset")

    let! canEditApp = service.CheckPermissionAsync (User "alice") AppEdit (SpaceObject "main")
    Assert.True(canEditApp, "Org admin should inherit edit_app via tupleToUserset")

    let! canDeleteApp = service.CheckPermissionAsync (User "alice") AppDelete (SpaceObject "main")
    Assert.True(canDeleteApp, "Org admin should inherit delete_app via tupleToUserset")

    let! canRunApp = service.CheckPermissionAsync (User "alice") AppRun (SpaceObject "main")
    Assert.True(canRunApp, "Org admin should inherit run_app via tupleToUserset")

    let! canCreateFolder = service.CheckPermissionAsync (User "alice") FolderCreate (SpaceObject "main")
    Assert.True(canCreateFolder, "Org admin should inherit create_folder via tupleToUserset")

    let! canEditFolder = service.CheckPermissionAsync (User "alice") FolderEdit (SpaceObject "main")
    Assert.True(canEditFolder, "Org admin should inherit edit_folder via tupleToUserset")

    let! canDeleteFolder = service.CheckPermissionAsync (User "alice") FolderDelete (SpaceObject "main")
    Assert.True(canDeleteFolder, "Org admin should inherit delete_folder via tupleToUserset")
}