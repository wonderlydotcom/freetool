namespace Freetool.Application.Commands

open Freetool.Domain.Entities
open Freetool.Domain.ValueObjects
open Freetool.Application.DTOs

type UserCommandResult =
    | UserResult of UserData
    | UsersResult of PagedResult<UserData>
    | UnitResult of unit

type UserCommand =
    | CreateUser of actorUserId: UserId * ValidatedUser
    | GetUserById of userId: string
    | GetUserByEmail of email: string
    | GetAllUsers of skip: int * take: int
    | DeleteUser of actorUserId: UserId * userId: string
    | UpdateUserName of actorUserId: UserId * userId: string * UpdateUserNameDto
    | UpdateUserEmail of actorUserId: UserId * userId: string * UpdateUserEmailDto
    | SetProfilePicture of actorUserId: UserId * userId: string * SetProfilePictureDto
    | RemoveProfilePicture of actorUserId: UserId * userId: string
    | InviteUser of actorUserId: UserId * InviteUserDto