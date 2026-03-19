module Freetool.Infrastructure.Tests.SpaceAuthorizationTests

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
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

// ============================================================================
// Space Moderator Permission Tests
// ============================================================================

[<Fact>]
let ``Space moderator should have all space permissions`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-moderator-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Alice the moderator of engineering space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "alice"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify moderator has all resource permissions
    let! canCreateResource = service.CheckPermissionAsync (User "alice") ResourceCreate (SpaceObject "engineering")
    Assert.True(canCreateResource, "Space moderator should have create_resource permission")

    let! canEditResource = service.CheckPermissionAsync (User "alice") ResourceEdit (SpaceObject "engineering")
    Assert.True(canEditResource, "Space moderator should have edit_resource permission")

    let! canDeleteResource = service.CheckPermissionAsync (User "alice") ResourceDelete (SpaceObject "engineering")
    Assert.True(canDeleteResource, "Space moderator should have delete_resource permission")

    // Act & Assert - Verify moderator has all app permissions
    let! canCreateApp = service.CheckPermissionAsync (User "alice") AppCreate (SpaceObject "engineering")
    Assert.True(canCreateApp, "Space moderator should have create_app permission")

    let! canEditApp = service.CheckPermissionAsync (User "alice") AppEdit (SpaceObject "engineering")
    Assert.True(canEditApp, "Space moderator should have edit_app permission")

    let! canDeleteApp = service.CheckPermissionAsync (User "alice") AppDelete (SpaceObject "engineering")
    Assert.True(canDeleteApp, "Space moderator should have delete_app permission")

    let! canRunApp = service.CheckPermissionAsync (User "alice") AppRun (SpaceObject "engineering")
    Assert.True(canRunApp, "Space moderator should have run_app permission")

    // Act & Assert - Verify moderator has all folder permissions
    let! canCreateFolder = service.CheckPermissionAsync (User "alice") FolderCreate (SpaceObject "engineering")
    Assert.True(canCreateFolder, "Space moderator should have create_folder permission")

    let! canEditFolder = service.CheckPermissionAsync (User "alice") FolderEdit (SpaceObject "engineering")
    Assert.True(canEditFolder, "Space moderator should have edit_folder permission")

    let! canDeleteFolder = service.CheckPermissionAsync (User "alice") FolderDelete (SpaceObject "engineering")
    Assert.True(canDeleteFolder, "Space moderator should have delete_folder permission")

    // Act & Assert - Verify moderator can rename space
    let! canRename = service.CheckPermissionAsync (User "alice") SpaceRename (SpaceObject "engineering")
    Assert.True(canRename, "Space moderator should have rename permission")
}

[<Fact>]
let ``Space member should NOT have permissions by default`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-member-no-perms-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Bob a member of engineering space (not moderator)
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "bob"
                    Relation = SpaceMember
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify member does NOT have create/edit/delete permissions
    let! canCreateResource = service.CheckPermissionAsync (User "bob") ResourceCreate (SpaceObject "engineering")
    Assert.False(canCreateResource, "Space member should NOT have create_resource permission")

    let! canEditResource = service.CheckPermissionAsync (User "bob") ResourceEdit (SpaceObject "engineering")
    Assert.False(canEditResource, "Space member should NOT have edit_resource permission")

    let! canDeleteResource = service.CheckPermissionAsync (User "bob") ResourceDelete (SpaceObject "engineering")
    Assert.False(canDeleteResource, "Space member should NOT have delete_resource permission")

    let! canCreateApp = service.CheckPermissionAsync (User "bob") AppCreate (SpaceObject "engineering")
    Assert.False(canCreateApp, "Space member should NOT have create_app permission")

    let! canEditApp = service.CheckPermissionAsync (User "bob") AppEdit (SpaceObject "engineering")
    Assert.False(canEditApp, "Space member should NOT have edit_app permission")

    let! canDeleteApp = service.CheckPermissionAsync (User "bob") AppDelete (SpaceObject "engineering")
    Assert.False(canDeleteApp, "Space member should NOT have delete_app permission")

    let! canRename = service.CheckPermissionAsync (User "bob") SpaceRename (SpaceObject "engineering")
    Assert.False(canRename, "Space member should NOT have rename permission")
}

// ============================================================================
// Organization Admin Permission Tests
// ============================================================================

[<Fact>]
let ``Organization admin should have all permissions on any space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-org-admin-space-{Guid.NewGuid()}"
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

    // Act & Assert - Verify org admin has all permissions on any space
    let! canCreateResource = service.CheckPermissionAsync (User "carol") ResourceCreate (SpaceObject "engineering")
    Assert.True(canCreateResource, "Org admin should have create_resource permission on any space")

    let! canEditResource = service.CheckPermissionAsync (User "carol") ResourceEdit (SpaceObject "engineering")
    Assert.True(canEditResource, "Org admin should have edit_resource permission on any space")

    let! canDeleteResource = service.CheckPermissionAsync (User "carol") ResourceDelete (SpaceObject "engineering")
    Assert.True(canDeleteResource, "Org admin should have delete_resource permission on any space")

    let! canCreateApp = service.CheckPermissionAsync (User "carol") AppCreate (SpaceObject "engineering")
    Assert.True(canCreateApp, "Org admin should have create_app permission on any space")

    let! canEditApp = service.CheckPermissionAsync (User "carol") AppEdit (SpaceObject "engineering")
    Assert.True(canEditApp, "Org admin should have edit_app permission on any space")

    let! canDeleteApp = service.CheckPermissionAsync (User "carol") AppDelete (SpaceObject "engineering")
    Assert.True(canDeleteApp, "Org admin should have delete_app permission on any space")

    let! canRunApp = service.CheckPermissionAsync (User "carol") AppRun (SpaceObject "engineering")
    Assert.True(canRunApp, "Org admin should have run_app permission on any space")

    let! canCreateFolder = service.CheckPermissionAsync (User "carol") FolderCreate (SpaceObject "engineering")
    Assert.True(canCreateFolder, "Org admin should have create_folder permission on any space")

    let! canEditFolder = service.CheckPermissionAsync (User "carol") FolderEdit (SpaceObject "engineering")
    Assert.True(canEditFolder, "Org admin should have edit_folder permission on any space")

    let! canDeleteFolder = service.CheckPermissionAsync (User "carol") FolderDelete (SpaceObject "engineering")
    Assert.True(canDeleteFolder, "Org admin should have delete_folder permission on any space")
}

[<Fact>]
let ``Only org admin can delete spaces`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-delete-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Dave the moderator of engineering space
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

    // Act & Assert - Moderator should NOT be able to delete space
    let! moderatorCanDelete = service.CheckPermissionAsync (User "dave") SpaceDelete (SpaceObject "engineering")
    Assert.False(moderatorCanDelete, "Space moderator should NOT be able to delete space")

    // Act & Assert - Org admin should be able to delete space
    let! adminCanDelete = service.CheckPermissionAsync (User "eve") SpaceDelete (SpaceObject "engineering")
    Assert.True(adminCanDelete, "Org admin should be able to delete space")
}

[<Fact>]
let ``Only org admin can create spaces`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-create-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Frank an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "frank"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Make Grace a regular user (not admin)
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "grace"
                    Relation = SpaceMember
                    Object = SpaceObject "existing-space"
                }
            ]
        )

    // Associate organization with space creation
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = Organization "default"
                    Relation = SpaceOrganization
                    Object = SpaceObject "new-space"
                }
            ]
        )

    // Act & Assert - Org admin should be able to create spaces
    let! adminCanCreate = service.CheckPermissionAsync (User "frank") SpaceCreate (SpaceObject "new-space")
    Assert.True(adminCanCreate, "Org admin should be able to create spaces")

    // Act & Assert - Regular user should NOT be able to create spaces
    let! userCanCreate = service.CheckPermissionAsync (User "grace") SpaceCreate (SpaceObject "new-space")
    Assert.False(userCanCreate, "Regular user should NOT be able to create spaces")
}

// ============================================================================
// Space Moderator Rename Tests
// ============================================================================

[<Fact>]
let ``Moderator can rename their space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-rename-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Henry the moderator of engineering space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "henry"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify moderator can rename their space
    let! canRename = service.CheckPermissionAsync (User "henry") SpaceRename (SpaceObject "engineering")
    Assert.True(canRename, "Space moderator should be able to rename their space")
}

[<Fact>]
let ``Member cannot rename space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-member-rename-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Ivan a member of engineering space (not moderator)
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

    // Act & Assert - Verify member cannot rename space
    let! canRename = service.CheckPermissionAsync (User "ivan") SpaceRename (SpaceObject "engineering")
    Assert.False(canRename, "Space member should NOT be able to rename space")
}

// ============================================================================
// Space Member Management Tests
// ============================================================================

[<Fact>]
let ``Moderator can add members to space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-add-member-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Jack the moderator of engineering space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "jack"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify moderator can add members
    let! canAddMember = service.CheckPermissionAsync (User "jack") SpaceAddMember (SpaceObject "engineering")
    Assert.True(canAddMember, "Space moderator should be able to add members")
}

[<Fact>]
let ``Moderator can remove members from space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-remove-member-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Kate the moderator of engineering space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "kate"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify moderator can remove members
    let! canRemoveMember = service.CheckPermissionAsync (User "kate") SpaceRemoveMember (SpaceObject "engineering")
    Assert.True(canRemoveMember, "Space moderator should be able to remove members")
}

[<Fact>]
let ``Member cannot add or remove other members`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-member-mgmt-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Larry a member of engineering space (not moderator)
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "larry"
                    Relation = SpaceMember
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Verify member cannot add members
    let! canAddMember = service.CheckPermissionAsync (User "larry") SpaceAddMember (SpaceObject "engineering")
    Assert.False(canAddMember, "Space member should NOT be able to add members")

    // Act & Assert - Verify member cannot remove members
    let! canRemoveMember = service.CheckPermissionAsync (User "larry") SpaceRemoveMember (SpaceObject "engineering")
    Assert.False(canRemoveMember, "Space member should NOT be able to remove members")
}

// ============================================================================
// Space Member Granted Permission Tests
// ============================================================================

[<Fact>]
let ``Space member with granted permission can access resource`` () : Task = task {
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

    // Make Mike a space member
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "mike"
                    Relation = SpaceMember
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Grant Mike specific permission on space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "mike"
                    Relation = AppRun
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act
    let! canRunApp = service.CheckPermissionAsync (User "mike") AppRun (SpaceObject "engineering")
    let! canEditApp = service.CheckPermissionAsync (User "mike") AppEdit (SpaceObject "engineering")

    // Assert
    Assert.True(canRunApp, "User should have explicitly granted run_app permission")
    Assert.False(canEditApp, "User should NOT have edit_app permission (not granted)")
}

// ============================================================================
// Space Inheritance via Organization Tests
// ============================================================================

[<Fact>]
let ``Org admin inherits space permissions via tupleToUserset`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-org-inherit-space-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // 1. Make Nancy an organization admin
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "nancy"
                    Relation = OrganizationAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // 2. Associate space with organization (NOT direct grants)
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

    // 3. Check permissions - should inherit via tupleToUserset
    let! canCreateResource = service.CheckPermissionAsync (User "nancy") ResourceCreate (SpaceObject "main")
    Assert.True(canCreateResource, "Org admin should inherit create_resource via tupleToUserset")

    let! canEditResource = service.CheckPermissionAsync (User "nancy") ResourceEdit (SpaceObject "main")
    Assert.True(canEditResource, "Org admin should inherit edit_resource via tupleToUserset")

    let! canDeleteResource = service.CheckPermissionAsync (User "nancy") ResourceDelete (SpaceObject "main")
    Assert.True(canDeleteResource, "Org admin should inherit delete_resource via tupleToUserset")

    let! canCreateApp = service.CheckPermissionAsync (User "nancy") AppCreate (SpaceObject "main")
    Assert.True(canCreateApp, "Org admin should inherit create_app via tupleToUserset")

    let! canEditApp = service.CheckPermissionAsync (User "nancy") AppEdit (SpaceObject "main")
    Assert.True(canEditApp, "Org admin should inherit edit_app via tupleToUserset")

    let! canDeleteApp = service.CheckPermissionAsync (User "nancy") AppDelete (SpaceObject "main")
    Assert.True(canDeleteApp, "Org admin should inherit delete_app via tupleToUserset")

    let! canRunApp = service.CheckPermissionAsync (User "nancy") AppRun (SpaceObject "main")
    Assert.True(canRunApp, "Org admin should inherit run_app via tupleToUserset")

    let! canCreateFolder = service.CheckPermissionAsync (User "nancy") FolderCreate (SpaceObject "main")
    Assert.True(canCreateFolder, "Org admin should inherit create_folder via tupleToUserset")

    let! canEditFolder = service.CheckPermissionAsync (User "nancy") FolderEdit (SpaceObject "main")
    Assert.True(canEditFolder, "Org admin should inherit edit_folder via tupleToUserset")

    let! canDeleteFolder = service.CheckPermissionAsync (User "nancy") FolderDelete (SpaceObject "main")
    Assert.True(canDeleteFolder, "Org admin should inherit delete_folder via tupleToUserset")
}

// ============================================================================
// Cross-Space Permission Isolation Tests
// ============================================================================

[<Fact>]
let ``Moderator of one space cannot access another space`` () : Task = task {
    // Arrange - Create store, write model
    let serviceWithoutStore = createServiceWithoutStore ()

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-space-isolation-{Guid.NewGuid()}"
            }
        )

    let service = createServiceWithStore storeResponse.Id
    let! _ = service.WriteAuthorizationModelAsync()

    // Make Oscar the moderator of engineering space
    do!
        service.CreateRelationshipsAsync(
            [
                {
                    Subject = User "oscar"
                    Relation = SpaceModerator
                    Object = SpaceObject "engineering"
                }
            ]
        )

    // Act & Assert - Oscar should NOT have access to sales space
    let! canCreateInSales = service.CheckPermissionAsync (User "oscar") ResourceCreate (SpaceObject "sales")
    Assert.False(canCreateInSales, "Moderator of engineering should NOT have access to sales space")

    let! canEditInSales = service.CheckPermissionAsync (User "oscar") AppEdit (SpaceObject "sales")
    Assert.False(canEditInSales, "Moderator of engineering should NOT have edit access to sales space")

    let! canRenameInSales = service.CheckPermissionAsync (User "oscar") SpaceRename (SpaceObject "sales")
    Assert.False(canRenameInSales, "Moderator of engineering should NOT be able to rename sales space")
}