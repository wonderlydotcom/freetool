module Freetool.Api.Tests.ResourceControllerAuthTests

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

[<Fact>]
let ``CreateResource - User with create_resource permission can create resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-create-resource-allowed-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // Grant user create_resource permission on workspace
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = ResourceCreate
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if user can create resources
    let! canCreate = authService.CheckPermissionAsync $"user:{userId}" "create_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canCreate, "User with create_resource permission should be able to create resources")
}

[<Fact>]
let ``CreateResource - User without create_resource permission cannot create resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-create-resource-denied-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // User does NOT have create_resource permission

    // Act - Check if user can create resources
    let! canCreate = authService.CheckPermissionAsync $"user:{userId}" "create_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.False(canCreate, "User without create_resource permission should NOT be able to create resources")
}

[<Fact>]
let ``CreateResource - Team admin has create_resource permission via admin role`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-admin-create-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = $"team:{Guid.NewGuid()}"
    let workspaceId = Guid.NewGuid().ToString()

    // Make user a team admin
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = TeamAdmin
                    Object = teamId
                }
            ]
        )

    // Associate workspace with team
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = teamId
                    Relation = WorkspaceTeam
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if team admin can create resources
    let! canCreate = authService.CheckPermissionAsync $"user:{userId}" "create_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canCreate, "Team admin should have create_resource permission via admin role")
}

[<Fact>]
let ``UpdateResource - User with edit_resource permission can update resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-edit-resource-allowed-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // Grant user edit_resource permission on workspace
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = "edit_resource"
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if user can edit resources
    let! canEdit = authService.CheckPermissionAsync $"user:{userId}" "edit_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canEdit, "User with edit_resource permission should be able to update resources")
}

[<Fact>]
let ``UpdateResource - User without edit_resource permission cannot update resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-edit-resource-denied-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // User does NOT have edit_resource permission

    // Act - Check if user can edit resources
    let! canEdit = authService.CheckPermissionAsync $"user:{userId}" "edit_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.False(canEdit, "User without edit_resource permission should NOT be able to update resources")
}

[<Fact>]
let ``UpdateResource - Team admin has edit_resource permission via admin role`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-admin-edit-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = $"team:{Guid.NewGuid()}"
    let workspaceId = Guid.NewGuid().ToString()

    // Make user a team admin
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = TeamAdmin
                    Object = teamId
                }
            ]
        )

    // Associate workspace with team
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = teamId
                    Relation = WorkspaceTeam
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if team admin can edit resources
    let! canEdit = authService.CheckPermissionAsync $"user:{userId}" "edit_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canEdit, "Team admin should have edit_resource permission via admin role")
}

[<Fact>]
let ``DeleteResource - User with delete_resource permission can delete resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-delete-resource-allowed-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // Grant user delete_resource permission on workspace
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = "delete_resource"
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if user can delete resources
    let! canDelete = authService.CheckPermissionAsync $"user:{userId}" "delete_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canDelete, "User with delete_resource permission should be able to delete resources")
}

[<Fact>]
let ``DeleteResource - User without delete_resource permission cannot delete resources`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-delete-resource-denied-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // User does NOT have delete_resource permission

    // Act - Check if user can delete resources
    let! canDelete = authService.CheckPermissionAsync $"user:{userId}" "delete_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.False(canDelete, "User without delete_resource permission should NOT be able to delete resources")
}

[<Fact>]
let ``DeleteResource - Team admin has delete_resource permission via admin role`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-team-admin-delete-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let teamId = $"team:{Guid.NewGuid()}"
    let workspaceId = Guid.NewGuid().ToString()

    // Make user a team admin
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = TeamAdmin
                    Object = teamId
                }
            ]
        )

    // Associate workspace with team
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = teamId
                    Relation = WorkspaceTeam
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act - Check if team admin can delete resources
    let! canDelete = authService.CheckPermissionAsync $"user:{userId}" "delete_resource" $"workspace:{workspaceId}"

    // Assert
    Assert.True(canDelete, "Team admin should have delete_resource permission via admin role")
}

[<Fact>]
let ``Global admin has all resource permissions on any workspace`` () : Task = task {
    // Arrange - Create store and write model
    let serviceWithoutStore = createServiceWithoutStore ()
    let storeGuid = Guid.NewGuid().ToString("N")

    let! storeResponse =
        serviceWithoutStore.CreateStoreAsync(
            {
                Name = $"test-global-admin-resources-{storeGuid}"
            }
        )

    let authService = createServiceWithStore storeResponse.Id
    let! _ = authService.WriteAuthorizationModelAsync()

    let userId = Guid.NewGuid().ToString()
    let workspaceId = Guid.NewGuid().ToString()

    // Make user a global admin
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    User = $"user:{userId}"
                    Relation = TeamAdmin
                    Object = OrganizationObject "default"
                }
            ]
        )

    // Grant global admins permissions on workspace
    do!
        authService.CreateRelationshipsAsync(
            [
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = ResourceCreate
                    Object = $"workspace:{workspaceId}"
                }
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = "edit_resource"
                    Object = $"workspace:{workspaceId}"
                }
                {
                    Subject = UserSetFromRelation("organization", "default", "admin")
                    Relation = "delete_resource"
                    Object = $"workspace:{workspaceId}"
                }
            ]
        )

    // Act & Assert - Verify global admin has all resource permissions
    let! canCreate = authService.CheckPermissionAsync $"user:{userId}" "create_resource" $"workspace:{workspaceId}"

    Assert.True(canCreate, "Global admin should have create_resource permission")

    let! canEdit = authService.CheckPermissionAsync $"user:{userId}" "edit_resource" $"workspace:{workspaceId}"
    Assert.True(canEdit, "Global admin should have edit_resource permission")

    let! canDelete = authService.CheckPermissionAsync $"user:{userId}" "delete_resource" $"workspace:{workspaceId}"

    Assert.True(canDelete, "Global admin should have delete_resource permission")
}