namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities

type ISpaceRepository =
    /// Gets a Space by its ID
    abstract member GetByIdAsync: SpaceId -> Task<ValidatedSpace option>

    /// Gets a Space by its name
    abstract member GetByNameAsync: string -> Task<ValidatedSpace option>

    /// Gets all Spaces with pagination
    abstract member GetAllAsync: skip: int -> take: int -> Task<ValidatedSpace list>

    /// Gets all Spaces that a user is a member of
    abstract member GetByUserIdAsync: UserId -> Task<ValidatedSpace list>

    /// Gets all Spaces where the user is the moderator
    abstract member GetByModeratorUserIdAsync: UserId -> Task<ValidatedSpace list>

    /// Adds a new Space (saves data and events atomically)
    abstract member AddAsync: ValidatedSpace -> Task<Result<unit, DomainError>>

    /// Updates an existing Space (saves data and events atomically)
    abstract member UpdateAsync: ValidatedSpace -> Task<Result<unit, DomainError>>

    /// Deletes a Space (soft delete, saves event atomically)
    abstract member DeleteAsync: ValidatedSpace -> Task<Result<unit, DomainError>>

    /// Checks if a Space exists by ID
    abstract member ExistsAsync: SpaceId -> Task<bool>

    /// Checks if a Space with the given name exists
    abstract member ExistsByNameAsync: string -> Task<bool>

    /// Gets the total count of Spaces
    abstract member GetCountAsync: unit -> Task<int>