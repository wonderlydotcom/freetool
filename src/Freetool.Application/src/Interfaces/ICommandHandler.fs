namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain
open Freetool.Application.Commands

type ICommandHandler =
    abstract member HandleCommand: IUserRepository -> UserCommand -> Task<Result<UserCommandResult, DomainError>>