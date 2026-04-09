namespace Freetool.Infrastructure.Services

open System.Threading.Tasks
open Microsoft.Extensions.Logging
open OpenFga.Sdk.Client
open OpenFga.Sdk.Client.Model
open OpenFga.Sdk.Model
open Freetool.Application.Interfaces

/// OpenFGA service implementation for fine-grained authorization
type OpenFgaService(apiUrl: string, logger: ILogger<OpenFgaService>, ?storeId: string) =

    let isDuplicateTupleWriteError (ex: exn) =
        ex.Message.Contains("cannot write a tuple which already exists")

    /// Creates a new OpenFGA client instance without store ID (for store creation)
    let createClientWithoutStore () =
        let configuration = ClientConfiguration(ApiUrl = apiUrl)
        new OpenFgaClient(configuration)

    /// Creates a new OpenFGA client instance with store ID (for store operations)
    let createClient () =
        let configuration = ClientConfiguration(ApiUrl = apiUrl)

        // Set store ID if provided
        match storeId with
        | Some id -> configuration.StoreId <- id
        | None -> ()

        new OpenFgaClient(configuration)

    interface IAuthorizationService with
        /// Creates a new OpenFGA store
        member _.CreateStoreAsync(request: CreateStoreRequest) : Task<StoreResponse> = task {
            use client = createClientWithoutStore ()
            let createRequest = ClientCreateStoreRequest(Name = request.Name)
            let! response = client.CreateStore(createRequest)

            return {
                Id = response.Id
                Name = response.Name
            }
        }

        /// Initializes the organization with an admin user
        /// This method is idempotent - if the tuple already exists, it succeeds silently
        member this.InitializeOrganizationAsync (organizationId: string) (adminUserId: string) : Task<unit> = task {
            logger.LogDebug(
                "InitializeOrganizationAsync called with orgId={OrganizationId}, userId={UserId}",
                organizationId,
                adminUserId
            )

            // Create the organization admin relationship tuple
            let tuple = {
                Subject = User adminUserId
                Relation = OrganizationAdmin
                Object = OrganizationObject organizationId
            }

            let userStr = AuthTypes.subjectToString tuple.Subject
            let relationStr = AuthTypes.relationToString tuple.Relation
            let objectStr = AuthTypes.objectToString tuple.Object

            logger.LogDebug("Creating relationship: {Object}#{Relation}@{User}", objectStr, relationStr, userStr)

            try
                do! (this :> IAuthorizationService).CreateRelationshipsAsync([ tuple ])
                logger.LogDebug("Relationship created successfully")
            with ex when isDuplicateTupleWriteError ex ->
                // Tuple already exists, that's fine - we're idempotent
                logger.LogDebug("Relationship already exists (idempotent success)")
        }

        /// Writes the authorization model to the store
        member _.WriteAuthorizationModelAsync() : Task<AuthorizationModelResponse> = task {
            use client = createClient ()

            // Define the authorization model
            let typeDefinitions = ResizeArray<TypeDefinition>()

            // Type: user (base type, no relations needed)
            typeDefinitions.Add(TypeDefinition(Type = "user"))

            // Type: organization (for global admins)
            let orgRelations = System.Collections.Generic.Dictionary<string, Userset>()
            orgRelations.["admin"] <- Userset(varThis = obj ())

            let orgMetadata = System.Collections.Generic.Dictionary<string, RelationMetadata>()

            orgMetadata.["admin"] <-
                RelationMetadata(DirectlyRelatedUserTypes = ResizeArray([ RelationReference(Type = "user") ]))

            typeDefinitions.Add(
                TypeDefinition(
                    Type = "organization",
                    Relations = orgRelations,
                    Metadata = Metadata(relations = orgMetadata)
                )
            )

            // Type: space (replaces both team and workspace - unified entity)
            let spaceRelations = System.Collections.Generic.Dictionary<string, Userset>()

            // Core relations
            spaceRelations.["member"] <- Userset(varThis = obj ()) // Members of the space
            spaceRelations.["moderator"] <- Userset(varThis = obj ()) // Exactly 1 moderator per space
            spaceRelations.["organization"] <- Userset(varThis = obj ()) // Org reference for derived permissions

            // Helper to create TupleToUserset for organization admin check
            let createOrgAdminTupleToUserset () =
                let orgAdminComputedUserset = ObjectRelation()
                orgAdminComputedUserset.Relation <- "admin"

                let orgTupleToUserset = TupleToUserset()
                orgTupleToUserset.Tupleset <- ObjectRelation(Object = "", Relation = "organization")
                orgTupleToUserset.ComputedUserset <- orgAdminComputedUserset
                orgTupleToUserset

            // Helper to create permission userset: union of (direct, moderator, org admin)
            let createModeratorOrOrgAdminUserset () =
                // Moderator check via computedUserset
                let moderatorUserset =
                    Userset(computedUserset = ObjectRelation(Relation = "moderator"))

                // Organization admin check: admin from organization relation
                let orgTupleToUserset = createOrgAdminTupleToUserset ()

                let unionUsersets = Usersets()

                unionUsersets.Child <-
                    ResizeArray(
                        [
                            Userset(varThis = obj ()) // Direct assignment
                            moderatorUserset // Space moderator
                            Userset(tupleToUserset = orgTupleToUserset)
                        ] // Organization admin
                    )

                Userset(union = unionUsersets)

            // Permissions derived from moderator OR org admin
            // Resource permissions
            spaceRelations.["create_resource"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["edit_resource"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["delete_resource"] <- createModeratorOrOrgAdminUserset ()

            // App permissions
            spaceRelations.["create_app"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["edit_app"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["delete_app"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["run_app"] <- createModeratorOrOrgAdminUserset ()

            // Dashboard permissions
            spaceRelations.["create_dashboard"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["edit_dashboard"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["delete_dashboard"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["run_dashboard"] <- createModeratorOrOrgAdminUserset ()

            // Folder permissions
            spaceRelations.["create_folder"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["edit_folder"] <- createModeratorOrOrgAdminUserset ()
            spaceRelations.["delete_folder"] <- createModeratorOrOrgAdminUserset ()

            // Space management permissions
            spaceRelations.["rename"] <- createModeratorOrOrgAdminUserset () // Moderator or org admin can rename space
            spaceRelations.["add_member"] <- createModeratorOrOrgAdminUserset () // Moderator or org admin can add members
            spaceRelations.["remove_member"] <- createModeratorOrOrgAdminUserset () // Moderator or org admin can remove members

            // Only org admin can create/delete spaces
            let orgAdminOnlyTupleToUserset = createOrgAdminTupleToUserset ()
            spaceRelations.["create"] <- Userset(tupleToUserset = orgAdminOnlyTupleToUserset)
            spaceRelations.["delete"] <- Userset(tupleToUserset = orgAdminOnlyTupleToUserset)

            // Define metadata for space relations
            let spaceMetadata =
                System.Collections.Generic.Dictionary<string, RelationMetadata>()

            spaceMetadata.["member"] <-
                RelationMetadata(DirectlyRelatedUserTypes = ResizeArray([ RelationReference(Type = "user") ]))

            spaceMetadata.["moderator"] <-
                RelationMetadata(DirectlyRelatedUserTypes = ResizeArray([ RelationReference(Type = "user") ]))

            spaceMetadata.["organization"] <-
                RelationMetadata(DirectlyRelatedUserTypes = ResizeArray([ RelationReference(Type = "organization") ]))

            // Add metadata for permissions that can be directly assigned or inherited
            // Note: "create" and "delete" are purely computed (org admin via tuple lookup only)
            // and should NOT have DirectlyRelatedUserTypes metadata
            for permission in
                [
                    "create_resource"
                    "edit_resource"
                    "delete_resource"
                    "create_app"
                    "edit_app"
                    "delete_app"
                    "run_app"
                    "create_dashboard"
                    "edit_dashboard"
                    "delete_dashboard"
                    "run_dashboard"
                    "create_folder"
                    "edit_folder"
                    "delete_folder"
                    "rename"
                    "add_member"
                    "remove_member"
                ] do
                spaceMetadata.[permission] <-
                    RelationMetadata(
                        DirectlyRelatedUserTypes =
                            ResizeArray(
                                [
                                    RelationReference(Type = "user")
                                    RelationReference(Type = "space", Relation = "member")
                                    RelationReference(Type = "organization", Relation = "admin")
                                ]
                            )
                    )

            typeDefinitions.Add(
                TypeDefinition(
                    Type = "space",
                    Relations = spaceRelations,
                    Metadata = Metadata(relations = spaceMetadata)
                )
            )

            let body =
                ClientWriteAuthorizationModelRequest(SchemaVersion = "1.1", TypeDefinitions = typeDefinitions)

            let! response = client.WriteAuthorizationModel(body)

            return {
                AuthorizationModelId = response.AuthorizationModelId
            }
        }

        /// Creates new relationship tuple(s). Idempotent - succeeds if tuples already exist.
        member _.CreateRelationshipsAsync(tuples: RelationshipTuple list) : Task<unit> = task {
            use client = createClient ()

            let writes =
                tuples
                |> List.map (fun t ->
                    let (user, relation, object) = RelationshipTuple.toStrings t
                    ClientTupleKey(User = user, Relation = relation, Object = object))
                |> ResizeArray

            let body = ClientWriteRequest(Writes = writes)

            try
                let! _ = client.Write(body)
                return ()
            with ex when isDuplicateTupleWriteError ex ->
                // Tuple already exists - idempotent success
                return ()
        }

        /// Updates relationships by adding and/or removing tuples in a single transaction
        member _.UpdateRelationshipsAsync(request: UpdateRelationshipsRequest) : Task<unit> = task {
            use client = createClient ()

            let writes =
                request.TuplesToAdd
                |> List.map (fun t ->
                    let (user, relation, object) = RelationshipTuple.toStrings t
                    ClientTupleKey(User = user, Relation = relation, Object = object))
                |> ResizeArray

            let deletes =
                request.TuplesToRemove
                |> List.map (fun t ->
                    let (user, relation, object) = RelationshipTuple.toStrings t
                    ClientTupleKeyWithoutCondition(User = user, Relation = relation, Object = object))
                |> ResizeArray

            let body = ClientWriteRequest(Writes = writes, Deletes = deletes)

            try
                let! _ = client.Write(body)
                return ()
            with ex when isDuplicateTupleWriteError ex && request.TuplesToRemove.IsEmpty ->
                // Add-only updates are safe to treat idempotently when the target tuple already exists.
                return ()
        }

        /// Deletes relationship tuple(s)
        member _.DeleteRelationshipsAsync(tuples: RelationshipTuple list) : Task<unit> = task {
            use client = createClient ()

            let deletes =
                tuples
                |> List.map (fun t ->
                    let (user, relation, object) = RelationshipTuple.toStrings t
                    ClientTupleKeyWithoutCondition(User = user, Relation = relation, Object = object))
                |> ResizeArray

            let body = ClientWriteRequest(Deletes = deletes)
            let! _ = client.Write(body)
            return ()
        }

        /// Checks if a user has a specific permission on an object
        member _.CheckPermissionAsync
            (subject: AuthSubject)
            (relation: AuthRelation)
            (object: AuthObject)
            : Task<bool> =
            task {
                use client = createClient ()

                let user = AuthTypes.subjectToString subject
                let relationStr = AuthTypes.relationToString relation
                let objectStr = AuthTypes.objectToString object

                logger.LogDebug("Checking permission: {Object}#{Relation}@{User}", objectStr, relationStr, user)

                let body =
                    ClientCheckRequest(User = user, Relation = relationStr, Object = objectStr)

                let! response = client.Check(body)
                let allowed = response.Allowed.GetValueOrDefault(false)

                logger.LogDebug("Permission check response: Allowed={Allowed}", allowed)
                return allowed
            }

        /// Checks if a store with the given ID exists
        member _.StoreExistsAsync(storeId: string) : Task<bool> = task {
            use client = createClientWithoutStore ()
            let request = ClientListStoresRequest()
            let mutable continuationToken = null
            let mutable storeExists = false
            let mutable hasMorePages = true

            while hasMorePages && not storeExists do
                let options = ClientListStoresOptions()
                options.ContinuationToken <- continuationToken

                let! response = client.ListStores(request, options)

                storeExists <- response.Stores |> Seq.exists (fun s -> s.Id = storeId)
                continuationToken <- response.ContinuationToken
                hasMorePages <- not (System.String.IsNullOrEmpty continuationToken)

            return storeExists
        }

        /// Batch checks multiple permissions for a subject on an object
        member this.BatchCheckPermissionsAsync
            (subject: AuthSubject)
            (relations: AuthRelation list)
            (object: AuthObject)
            : Task<Map<AuthRelation, bool>> =
            task {
                // Use parallel check calls for each relation
                let! results =
                    relations
                    |> List.map (fun relation -> task {
                        let! allowed = (this :> IAuthorizationService).CheckPermissionAsync subject relation object

                        return (relation, allowed)
                    })
                    |> Task.WhenAll

                return results |> Array.toList |> Map.ofList
            }