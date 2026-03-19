namespace Freetool.Application.Interfaces

open System.Threading.Tasks
open Freetool.Domain

/// Unified command handler interface.
/// All handlers implement this interface with their specific command and result types.
/// Repository dependencies are injected via constructor, not method parameters.
type ICommandHandler<'TCommand, 'TResult> =
    abstract member HandleCommand: 'TCommand -> Task<Result<'TResult, DomainError>>