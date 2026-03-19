namespace Freetool.Application.Mappers

open System
open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

module UserMapper =
    let fromCreateDto (dto: CreateUserDto) : UnvalidatedUser = {
        State = {
            Id = UserId.NewId()
            Name = dto.Name
            Email = dto.Email
            ProfilePicUrl = dto.ProfilePicUrl
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
            IsDeleted = false
            InvitedAt = None
        }
        UncommittedEvents = []
    }

    let fromUpdateNameDto (dto: UpdateUserNameDto) (user: ValidatedUser) : UnvalidatedUser = {
        State = {
            user.State with
                Name = dto.Name
                UpdatedAt = DateTime.UtcNow
        }
        UncommittedEvents = []
    }

    let fromUpdateEmailDto (dto: UpdateUserEmailDto) (user: ValidatedUser) : UnvalidatedUser = {
        State = {
            user.State with
                Email = dto.Email
                UpdatedAt = DateTime.UtcNow
        }
        UncommittedEvents = []
    }

    let fromSetProfilePictureDto (dto: SetProfilePictureDto) (user: ValidatedUser) : UnvalidatedUser = {
        State = {
            user.State with
                ProfilePicUrl = Some(dto.ProfilePicUrl)
                UpdatedAt = DateTime.UtcNow
        }
        UncommittedEvents = []
    }