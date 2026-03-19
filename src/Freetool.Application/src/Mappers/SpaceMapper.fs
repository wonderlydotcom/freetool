namespace Freetool.Application.Mappers

open System
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

module SpaceMapper =
    /// Maps SpaceData to SpaceDto for API responses
    let toDto (spaceData: SpaceData) : SpaceDto = {
        Id = spaceData.Id.Value.ToString()
        Name = spaceData.Name
        ModeratorUserId = spaceData.ModeratorUserId.Value.ToString()
        MemberIds = spaceData.MemberIds |> List.map (fun id -> id.Value.ToString())
        CreatedAt = spaceData.CreatedAt
        UpdatedAt = spaceData.UpdatedAt
    }

    /// Maps a CreateSpaceDto to an UnvalidatedSpace
    /// Note: This creates a preliminary structure; validation and event generation
    /// should be done via Space.create in the domain layer
    let fromCreateDto (dto: CreateSpaceDto) : Result<UserId * string * UserId list, string> =
        // Parse moderator ID
        match Guid.TryParse dto.ModeratorUserId with
        | false, _ -> Error "Invalid moderator user ID format"
        | true, moderatorGuid ->
            let moderatorUserId = UserId.FromGuid moderatorGuid

            // Convert string MemberIds to UserId list, filtering out invalid GUIDs
            let memberIds =
                dto.MemberIds
                |> Option.defaultValue []
                |> List.choose (fun memberIdStr ->
                    match Guid.TryParse memberIdStr with
                    | true, guid -> Some(UserId.FromGuid guid)
                    | false, _ -> None)
                |> List.distinct
                // Filter out moderator from member list
                |> List.filter (fun id -> id <> moderatorUserId)

            Ok(moderatorUserId, dto.Name, memberIds)

    /// Maps a list of SpaceData to a PagedResult of SpaceDto
    let toPagedDto (spaces: SpaceData list) (totalCount: int) (skip: int) (take: int) : PagedResult<SpaceDto> = {
        Items = spaces |> List.map toDto
        TotalCount = totalCount
        Skip = skip
        Take = take
    }