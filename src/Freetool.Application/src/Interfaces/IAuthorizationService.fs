namespace Freetool.Application.Interfaces

open System
open System.Threading.Tasks

/// Request to create an OpenFGA store
type CreateStoreRequest = { Name: string }

/// Response from creating an OpenFGA store
type StoreResponse = { Id: string; Name: string }

/// Response from writing an authorization model
type AuthorizationModelResponse = { AuthorizationModelId: string }

/// Represents the subject (user field) in a relationship tuple
type AuthSubject =
    | User of userId: string
    | Space of spaceId: string
    | Organization of organizationId: string
    | UserSetFromRelation of objectType: string * objectId: string * relation: string

/// Represents a relation/permission in the authorization model
type AuthRelation =
    // Organization relations
    | OrganizationAdmin
    // Space relations (replaces Team + Workspace)
    | SpaceMember // Regular member of a space
    | SpaceModerator // The single moderator of a space (replaces team admin)
    | SpaceOrganization // Organization reference for derived permissions
    | SpaceCreate // Permission to create a space (org admin only)
    | SpaceRename // Permission to rename the space (moderator or org admin)
    | SpaceDelete // Permission to delete the space (org admin only)
    | SpaceAddMember // Permission to add members to a space (moderator or org admin)
    | SpaceRemoveMember // Permission to remove members from a space (moderator or org admin)
    // Resource permissions (apply to Space)
    | ResourceCreate
    | ResourceEdit
    | ResourceDelete
    // App permissions (apply to Space)
    | AppCreate
    | AppEdit
    | AppDelete
    | AppRun
    // Dashboard permissions (apply to Space)
    | DashboardCreate
    | DashboardEdit
    | DashboardDelete
    | DashboardRun
    // Folder permissions (apply to Space)
    | FolderCreate
    | FolderEdit
    | FolderDelete

/// Represents the object (resource) in a relationship tuple
type AuthObject =
    | UserObject of userId: string
    | SpaceObject of spaceId: string
    | OrganizationObject of organizationId: string

/// Helper module for converting between strongly-typed and string representations
module AuthTypes =
    /// Converts an AuthSubject to OpenFGA string format
    let subjectToString (subject: AuthSubject) : string =
        match subject with
        | User userId -> $"user:{userId}"
        | Space spaceId -> $"space:{spaceId}"
        | Organization orgId -> $"organization:{orgId}"
        | UserSetFromRelation(objectType, objectId, relation) -> $"{objectType}:{objectId}#{relation}"

    /// Converts an AuthRelation to OpenFGA string format
    let relationToString (relation: AuthRelation) : string =
        match relation with
        | OrganizationAdmin -> "admin"
        | SpaceMember -> "member"
        | SpaceModerator -> "moderator"
        | SpaceOrganization -> "organization"
        | SpaceCreate -> "create"
        | SpaceRename -> "rename"
        | SpaceDelete -> "delete"
        | SpaceAddMember -> "add_member"
        | SpaceRemoveMember -> "remove_member"
        | ResourceCreate -> "create_resource"
        | ResourceEdit -> "edit_resource"
        | ResourceDelete -> "delete_resource"
        | AppCreate -> "create_app"
        | AppEdit -> "edit_app"
        | AppDelete -> "delete_app"
        | AppRun -> "run_app"
        | DashboardCreate -> "create_dashboard"
        | DashboardEdit -> "edit_dashboard"
        | DashboardDelete -> "delete_dashboard"
        | DashboardRun -> "run_dashboard"
        | FolderCreate -> "create_folder"
        | FolderEdit -> "edit_folder"
        | FolderDelete -> "delete_folder"

    /// Converts an AuthObject to OpenFGA string format
    let objectToString (obj: AuthObject) : string =
        match obj with
        | UserObject userId -> $"user:{userId}"
        | SpaceObject spaceId -> $"space:{spaceId}"
        | OrganizationObject orgId -> $"organization:{orgId}"

    let private splitTypeAndId (value: string) =
        let parts = value.Split([| ':' |], 2, StringSplitOptions.None)

        match parts with
        | [| itemType; itemId |] -> Some(itemType, itemId)
        | _ -> None

    /// Attempts to parse an OpenFGA subject string into a typed AuthSubject.
    let tryParseSubject (value: string) : AuthSubject option =
        if String.IsNullOrWhiteSpace value then
            None
        elif value.Contains "#" then
            let parts = value.Split([| '#' |], 2, StringSplitOptions.None)

            match parts with
            | [| objectPart; relation |] ->
                splitTypeAndId objectPart
                |> Option.map (fun (objectType, objectId) -> UserSetFromRelation(objectType, objectId, relation))
            | _ -> None
        else
            match splitTypeAndId value with
            | Some("user", userId) -> Some(User userId)
            | Some("space", spaceId) -> Some(Space spaceId)
            | Some("organization", organizationId) -> Some(Organization organizationId)
            | _ -> None

    /// Attempts to parse an OpenFGA relation string into a typed AuthRelation.
    let tryParseRelation (value: string) : AuthRelation option =
        match value with
        | "admin" -> Some OrganizationAdmin
        | "member" -> Some SpaceMember
        | "moderator" -> Some SpaceModerator
        | "organization" -> Some SpaceOrganization
        | "create" -> Some SpaceCreate
        | "rename" -> Some SpaceRename
        | "delete" -> Some SpaceDelete
        | "add_member" -> Some SpaceAddMember
        | "remove_member" -> Some SpaceRemoveMember
        | "create_resource" -> Some ResourceCreate
        | "edit_resource" -> Some ResourceEdit
        | "delete_resource" -> Some ResourceDelete
        | "create_app" -> Some AppCreate
        | "edit_app" -> Some AppEdit
        | "delete_app" -> Some AppDelete
        | "run_app" -> Some AppRun
        | "create_dashboard" -> Some DashboardCreate
        | "edit_dashboard" -> Some DashboardEdit
        | "delete_dashboard" -> Some DashboardDelete
        | "run_dashboard" -> Some DashboardRun
        | "create_folder" -> Some FolderCreate
        | "edit_folder" -> Some FolderEdit
        | "delete_folder" -> Some FolderDelete
        | _ -> None

    /// Attempts to parse an OpenFGA object string into a typed AuthObject.
    let tryParseObject (value: string) : AuthObject option =
        match splitTypeAndId value with
        | Some("user", userId) -> Some(UserObject userId)
        | Some("space", spaceId) -> Some(SpaceObject spaceId)
        | Some("organization", organizationId) -> Some(OrganizationObject organizationId)
        | _ -> None

/// Represents a strongly-typed relationship tuple
type RelationshipTuple = {
    Subject: AuthSubject
    Relation: AuthRelation
    Object: AuthObject
}

/// Helper module for RelationshipTuple operations
module RelationshipTuple =
    /// Converts a RelationshipTuple to the legacy string-based format for OpenFGA SDK
    let toStrings (tuple: RelationshipTuple) : string * string * string =
        (AuthTypes.subjectToString tuple.Subject,
         AuthTypes.relationToString tuple.Relation,
         AuthTypes.objectToString tuple.Object)

    let toDisplayString (tuple: RelationshipTuple) : string =
        let (subject, relation, obj) = toStrings tuple
        $"{obj}#{relation}@{subject}"

/// Request to update relationships (add and/or remove tuples)
type UpdateRelationshipsRequest = {
    TuplesToAdd: RelationshipTuple list
    TuplesToRemove: RelationshipTuple list
}

/// Read-only interface for listing existing OpenFGA tuples on an object.
type IAuthorizationRelationshipReader =
    abstract member ReadRelationshipsAsync: object: AuthObject -> Task<RelationshipTuple list>

/// Interface for OpenFGA authorization operations
type IAuthorizationService =
    /// Creates a new OpenFGA store for authorization data
    abstract member CreateStoreAsync: CreateStoreRequest -> Task<StoreResponse>

    /// Writes the authorization model to the store
    abstract member WriteAuthorizationModelAsync: unit -> Task<AuthorizationModelResponse>

    /// Initializes the organization with an admin user
    abstract member InitializeOrganizationAsync: organizationId: string -> adminUserId: string -> Task<unit>

    /// Creates new relationship tuple(s)
    abstract member CreateRelationshipsAsync: RelationshipTuple list -> Task<unit>

    /// Updates relationships by adding and/or removing tuples in a single transaction
    abstract member UpdateRelationshipsAsync: UpdateRelationshipsRequest -> Task<unit>

    /// Deletes relationship tuple(s)
    abstract member DeleteRelationshipsAsync: RelationshipTuple list -> Task<unit>

    /// Checks if a user has a specific permission on an object
    abstract member CheckPermissionAsync:
        subject: AuthSubject -> relation: AuthRelation -> object: AuthObject -> Task<bool>

    /// Checks if a store with the given ID exists
    abstract member StoreExistsAsync: storeId: string -> Task<bool>

    /// Batch checks multiple permissions for a subject on an object
    abstract member BatchCheckPermissionsAsync:
        subject: AuthSubject -> relations: AuthRelation list -> object: AuthObject -> Task<Map<AuthRelation, bool>>