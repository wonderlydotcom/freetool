module Freetool.Api.Tests.AppControllerAuthTests

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Services
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

// Test configuration - assumes OpenFGA is running on localhost:8090
let openFgaApiUrl = "http://localhost:8090"

// Helper to create a test service without a store ID (for store creation)
let createServiceWithoutStore () =
    OpenFgaService(openFgaApiUrl, NullLogger<OpenFgaService>.Instance) :> IAuthorizationService

// Helper to create a test service with a store ID
let createServiceWithStore storeId =
    OpenFgaService(openFgaApiUrl, NullLogger<OpenFgaService>.Instance, storeId) :> IAuthorizationService

// Test setup helper to create a space with proper permissions
let setupSpaceWithPermissions (authService: IAuthorizationService) (userId: string) (permission: AuthRelation) = task {
    let spaceId = Guid.NewGuid().ToString()
    let orgId = "default"

    // Associate space with organization
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization orgId
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Grant permission to user
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = permission
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    return spaceId
}

[<Fact>]
let ``CreateApp endpoint - User with create_app permission can create app`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-create-app-allowed-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppCreate

    // Act - Check if user can create app on workspace
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppCreate
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canCreate = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.True(canCreate, "User with create_app permission should be allowed to create apps")
}

[<Fact>]
let ``CreateApp endpoint - User without create_app permission is denied`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-create-app-denied-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Act - Check if user (without permission) can create app
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppCreate
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canCreate = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.False(canCreate, "User without create_app permission should be denied")
}

[<Fact>]
let ``GetAppById endpoint - User with run_app permission can view app`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-get-app-allowed-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppRun

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppRun
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canView = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.True(canView, "User with run_app permission should be allowed to view apps")
}

[<Fact>]
let ``GetAppById endpoint - User without run_app permission is denied`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-get-app-denied-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppRun
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canView = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.False(canView, "User without run_app permission should be denied")
}

[<Fact>]
let ``UpdateAppName endpoint - User with edit_app permission can update`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-edit-app-allowed-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppEdit

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppEdit
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canEdit = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.True(canEdit, "User with edit_app permission should be allowed to update apps")
}

[<Fact>]
let ``UpdateAppName endpoint - User without edit_app permission is denied`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-edit-app-denied-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppEdit
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canEdit = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.False(canEdit, "User without edit_app permission should be denied")
}

[<Fact>]
let ``DeleteApp endpoint - User with delete_app permission can delete`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-delete-app-allowed-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppDelete

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppDelete
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canDelete = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.True(canDelete, "User with delete_app permission should be allowed to delete apps")
}

[<Fact>]
let ``DeleteApp endpoint - User without delete_app permission is denied`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-delete-app-denied-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppDelete
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canDelete = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.False(canDelete, "User without delete_app permission should be denied")
}

[<Fact>]
let ``RunApp endpoint - User with run_app permission can run`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-run-app-allowed-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppRun

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppRun
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canRun = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.True(canRun, "User with run_app permission should be allowed to run apps")
}

[<Fact>]
let ``RunApp endpoint - User without run_app permission is denied`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-run-app-denied-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Act
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppRun
    let obj: AuthObject = AuthObject.SpaceObject spaceId
    let! canRun = authService.CheckPermissionAsync subject relation obj

    // Assert
    Assert.False(canRun, "User without run_app permission should be denied")
}

[<Fact>]
let ``Team admin has all app permissions on workspace`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-admin-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Make user a space moderator of the space we'll check permissions on
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = SpaceModerator
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Associate space with organization
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization "default"
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Act & Assert - Verify user has all app permissions (as moderator of the space)
    let subject: AuthSubject = AuthSubject.User userId
    let obj: AuthObject = AuthObject.SpaceObject spaceId

    let relation1 = AppCreate
    let! canCreateApp = authService.CheckPermissionAsync subject relation1 obj
    Assert.True(canCreateApp, "Team admin should have create_app permission")

    let relation2 = AppEdit
    let! canEditApp = authService.CheckPermissionAsync subject relation2 obj
    Assert.True(canEditApp, "Team admin should have edit_app permission")

    let relation3 = AppDelete
    let! canDeleteApp = authService.CheckPermissionAsync subject relation3 obj
    Assert.True(canDeleteApp, "Team admin should have delete_app permission")

    let relation4 = AppRun
    let! canRunApp = authService.CheckPermissionAsync subject relation4 obj
    Assert.True(canRunApp, "Team admin should have run_app permission")
}

[<Fact>]
let ``Global admin has all app permissions on any workspace`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-global-admin-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Make user an organization admin
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = OrganizationAdmin
                    Object = AuthObject.OrganizationObject "default"
                }
            ]
        )

    // Associate space with organization (org admin gets permissions via tuple lookup)
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization "default"
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Act & Assert - Verify global admin has all permissions
    let subject: AuthSubject = AuthSubject.User userId
    let obj: AuthObject = AuthObject.SpaceObject spaceId

    let relation1 = AppCreate
    let! canCreateApp = authService.CheckPermissionAsync subject relation1 obj
    Assert.True(canCreateApp, "Global admin should have create_app permission")

    let relation2 = AppEdit
    let! canEditApp = authService.CheckPermissionAsync subject relation2 obj
    Assert.True(canEditApp, "Global admin should have edit_app permission")

    let relation3 = AppDelete
    let! canDeleteApp = authService.CheckPermissionAsync subject relation3 obj
    Assert.True(canDeleteApp, "Global admin should have delete_app permission")

    let relation4 = AppRun
    let! canRunApp = authService.CheckPermissionAsync subject relation4 obj
    Assert.True(canRunApp, "Global admin should have run_app permission")
}

[<Fact>]
let ``Team member with specific permission can only perform allowed actions`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-member-limited-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Make user a team member (not admin)
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = SpaceMember
                    Object = AuthObject.SpaceObject teamId
                }
            ]
        )

    // Associate workspace with team
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization teamId
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Grant only run_app permission
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = AppRun
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Act & Assert
    let subject: AuthSubject = AuthSubject.User userId
    let obj: AuthObject = AuthObject.SpaceObject spaceId

    let relation1 = AppRun
    let! canRunApp = authService.CheckPermissionAsync subject relation1 obj
    Assert.True(canRunApp, "Team member should have explicitly granted run_app permission")

    let relation2 = AppCreate
    let! canCreateApp = authService.CheckPermissionAsync subject relation2 obj
    Assert.False(canCreateApp, "Team member should NOT have create_app permission (not granted)")

    let relation3 = AppEdit
    let! canEditApp = authService.CheckPermissionAsync subject relation3 obj
    Assert.False(canEditApp, "Team member should NOT have edit_app permission (not granted)")

    let relation4 = AppDelete
    let! canDeleteApp = authService.CheckPermissionAsync subject relation4 obj
    Assert.False(canDeleteApp, "Team member should NOT have delete_app permission (not granted)")
}

[<Fact>]
let ``All update endpoints require edit_app permission`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-all-updates-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let! spaceId = setupSpaceWithPermissions authService userId AppEdit

    // Act - Verify that all these update operations use the same permission
    let subject: AuthSubject = AuthSubject.User userId
    let relation = AppEdit
    let obj: AuthObject = AuthObject.SpaceObject spaceId

    let! canUpdateName = authService.CheckPermissionAsync subject relation obj
    let! canUpdateInputs = authService.CheckPermissionAsync subject relation obj
    let! canUpdateQueryParams = authService.CheckPermissionAsync subject relation obj
    let! canUpdateBody = authService.CheckPermissionAsync subject relation obj
    let! canUpdateHeaders = authService.CheckPermissionAsync subject relation obj
    let! canUpdateUrlPath = authService.CheckPermissionAsync subject relation obj

    // Assert - All should be true with edit_app permission
    Assert.True(canUpdateName, "edit_app permission allows UpdateAppName")
    Assert.True(canUpdateInputs, "edit_app permission allows UpdateAppInputs")
    Assert.True(canUpdateQueryParams, "edit_app permission allows UpdateAppQueryParameters")
    Assert.True(canUpdateBody, "edit_app permission allows UpdateAppBody")
    Assert.True(canUpdateHeaders, "edit_app permission allows UpdateAppHeaders")
    Assert.True(canUpdateUrlPath, "edit_app permission allows UpdateAppUrlPath")
}

[<Fact>]
let ``GetApps endpoint - Team member belongs without run_app permission`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-list-apps-team-member-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Associate workspace with team
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization teamId
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Add user as a team member only
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.User userId
                    Relation = SpaceMember
                    Object = AuthObject.SpaceObject teamId
                }
            ]
        )

    // Act
    let! isSpaceMember =
        authService.CheckPermissionAsync (AuthSubject.User userId) SpaceMember (AuthObject.SpaceObject teamId)

    let! canRun = authService.CheckPermissionAsync (AuthSubject.User userId) AppRun (AuthObject.SpaceObject spaceId)

    // Assert
    Assert.True(isSpaceMember, "Team members should belong to the workspace even without app permissions")
    Assert.False(canRun, "Team membership alone should not grant run_app permission")
}

[<Fact>]
let ``GetApps endpoint - User outside team is not considered a member`` () : Task = task {
    // Arrange
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-list-apps-non-member-{Guid.NewGuid()}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = Guid.NewGuid().ToString()
    let spaceId = Guid.NewGuid().ToString()

    // Associate workspace with team only
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = AuthSubject.Organization teamId
                    Relation = SpaceOrganization
                    Object = AuthObject.SpaceObject spaceId
                }
            ]
        )

    // Act
    let! isSpaceMember =
        authService.CheckPermissionAsync (AuthSubject.User userId) SpaceMember (AuthObject.SpaceObject teamId)

    // Assert
    Assert.False(isSpaceMember, "Users outside the workspace team should not be considered members")
}