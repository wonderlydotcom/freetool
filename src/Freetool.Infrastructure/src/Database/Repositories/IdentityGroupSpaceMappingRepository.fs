namespace Freetool.Infrastructure.Database.Repositories

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Infrastructure.Database

type IdentityGroupSpaceMappingRepository(context: FreetoolDbContext) =

    let toDto
        (spaceNameLookup: Map<SpaceId, string>)
        (mapping: IdentityGroupSpaceMappingData)
        : IdentityGroupSpaceMappingDto =
        {
            Id = mapping.Id.ToString()
            GroupKey = mapping.GroupKey
            SpaceId = mapping.SpaceId.Value.ToString()
            SpaceName = spaceNameLookup |> Map.tryFind mapping.SpaceId
            IsActive = mapping.IsActive
            CreatedAt = mapping.CreatedAt
            UpdatedAt = mapping.UpdatedAt
        }

    interface IIdentityGroupSpaceMappingRepository with
        member _.GetAllAsync() : Task<IdentityGroupSpaceMappingDto list> = task {
            let! mappings =
                context.IdentityGroupSpaceMappings.OrderBy(fun m -> m.GroupKey).ThenBy(fun m -> m.SpaceId).ToListAsync()

            if mappings.Count = 0 then
                return []
            else
                let spaceIds =
                    mappings |> Seq.map (fun m -> m.SpaceId) |> Seq.distinct |> Seq.toList

                let! spaces =
                    context.Spaces.Where(fun s -> spaceIds.Contains(s.Id)).Select(fun s -> (s.Id, s.Name)).ToListAsync()

                let spaceNameLookup = spaces |> Seq.map (fun (id, name) -> (id, name)) |> Map.ofSeq
                return mappings |> Seq.map (toDto spaceNameLookup) |> Seq.toList
        }

        member _.GetSpaceIdsByGroupKeysAsync(groupKeys: string list) : Task<SpaceId list> = task {
            let normalizedKeys =
                groupKeys
                |> List.map (fun g -> g.Trim())
                |> List.filter (fun g -> not (String.IsNullOrWhiteSpace g))
                |> List.distinct

            if List.isEmpty normalizedKeys then
                return []
            else
                let! spaceIds =
                    context.IdentityGroupSpaceMappings
                        .Where(fun m -> m.IsActive && normalizedKeys.Contains(m.GroupKey))
                        .Select(fun m -> m.SpaceId)
                        .Distinct()
                        .ToListAsync()

                return spaceIds |> Seq.toList
        }

        member _.AddAsync
            (actorUserId: UserId)
            (groupKey: string)
            (spaceId: SpaceId)
            : Task<Result<IdentityGroupSpaceMappingDto, DomainError>> =
            task {
                let normalizedGroupKey = groupKey.Trim()

                if String.IsNullOrWhiteSpace normalizedGroupKey then
                    return Error(ValidationError "GroupKey cannot be empty")
                else
                    let! spaceExists = context.Spaces.AnyAsync(fun s -> s.Id = spaceId)

                    if not spaceExists then
                        return Error(NotFound "Space not found")
                    else
                        let! exists =
                            context.IdentityGroupSpaceMappings.AnyAsync(fun m ->
                                m.GroupKey = normalizedGroupKey && m.SpaceId = spaceId)

                        if exists then
                            return Error(Conflict "Mapping already exists for this group and space")
                        else
                            let now = DateTime.UtcNow

                            let mapping = {
                                Id = Guid.NewGuid()
                                GroupKey = normalizedGroupKey
                                SpaceId = spaceId
                                IsActive = true
                                CreatedByUserId = actorUserId
                                UpdatedByUserId = actorUserId
                                CreatedAt = now
                                UpdatedAt = now
                            }

                            context.IdentityGroupSpaceMappings.Add(mapping) |> ignore

                            try
                                let! _ = context.SaveChangesAsync()

                                let! spaceName =
                                    context.Spaces
                                        .Where(fun s -> s.Id = spaceId)
                                        .Select(fun s -> s.Name)
                                        .FirstOrDefaultAsync()

                                let spaceLookup = Map.ofList [ (spaceId, spaceName) ]
                                return Ok(toDto spaceLookup mapping)
                            with :? DbUpdateException as ex ->
                                return Error(Conflict $"Failed to create mapping: {ex.Message}")
            }

        member _.UpdateIsActiveAsync
            (actorUserId: UserId)
            (mappingId: Guid)
            (isActive: bool)
            : Task<Result<unit, DomainError>> =
            task {
                let! existing = context.IdentityGroupSpaceMappings.FirstOrDefaultAsync(fun m -> m.Id = mappingId)

                match Option.ofObj existing with
                | None -> return Error(NotFound "Mapping not found")
                | Some mapping ->
                    let updated = {
                        mapping with
                            IsActive = isActive
                            UpdatedByUserId = actorUserId
                            UpdatedAt = DateTime.UtcNow
                    }

                    context.Entry(mapping).CurrentValues.SetValues(updated)

                    try
                        let! _ = context.SaveChangesAsync()
                        return Ok()
                    with :? DbUpdateException as ex ->
                        return Error(Conflict $"Failed to update mapping: {ex.Message}")
            }

        member _.DeleteAsync(mappingId: Guid) : Task<Result<unit, DomainError>> = task {
            let! existing = context.IdentityGroupSpaceMappings.FirstOrDefaultAsync(fun m -> m.Id = mappingId)

            match Option.ofObj existing with
            | None -> return Error(NotFound "Mapping not found")
            | Some mapping ->
                context.IdentityGroupSpaceMappings.Remove(mapping) |> ignore

                try
                    let! _ = context.SaveChangesAsync()
                    return Ok()
                with :? DbUpdateException as ex ->
                    return Error(Conflict $"Failed to delete mapping: {ex.Message}")
        }