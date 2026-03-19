namespace Freetool.Application.Handlers

open System
open System.Threading.Tasks
open Freetool.Domain
open Freetool.Domain.ValueObjects
open Freetool.Domain.Entities
open Freetool.Application.Interfaces
open Freetool.Application.Commands
open Freetool.Application.DTOs

module UserHandler =

    let handleCommand
        (userRepository: IUserRepository)
        (command: UserCommand)
        : Task<Result<UserCommandResult, DomainError>> =
        task {
            match command with
            | CreateUser(actorUserId, validatedUser) ->
                // Check if email already exists
                let email =
                    Email.Create(Some(User.getEmail validatedUser))
                    |> function
                        | Ok e -> e
                        | Error err ->
                            failwith $"Invariant violation: ValidatedUser should have valid email, but got: {err}"

                let! existsByEmail = userRepository.ExistsByEmailAsync email

                if existsByEmail then
                    return Error(Conflict "A user with this email already exists")
                else
                    // Create event-aware user from the validated user with actorUserId
                    let eventAwareUser =
                        User.create (User.getName validatedUser) email (User.getProfilePicUrl validatedUser)

                    // Save user and events atomically
                    match! userRepository.AddAsync eventAwareUser with
                    | Error error -> return Error error
                    | Ok() -> return Ok(UserResult(eventAwareUser.State))

            | DeleteUser(actorUserId, userId) ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user ->
                        // Mark user for deletion to create the delete event with actorUserId
                        let userWithDeleteEvent = User.markForDeletion actorUserId user

                        // Delete user and save event atomically
                        match! userRepository.DeleteAsync userWithDeleteEvent with
                        | Error error -> return Error error
                        | Ok() -> return Ok(UserCommandResult.UnitResult())

            | UpdateUserName(actorUserId, userId, dto) ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user ->
                        // Update name using domain method with actor (automatically creates event)
                        match User.updateName actorUserId (Some dto.Name) user with
                        | Error error -> return Error error
                        | Ok updatedUser ->
                            // Save user and events atomically
                            match! userRepository.UpdateAsync updatedUser with
                            | Error error -> return Error error
                            | Ok() -> return Ok(UserResult(updatedUser.State))

            | UpdateUserEmail(actorUserId, userId, dto) ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user ->
                        // Update email using domain method with actor (automatically creates event)
                        match User.updateEmail actorUserId dto.Email user with
                        | Error error -> return Error error
                        | Ok updatedUser ->
                            // Save user and events atomically
                            match! userRepository.UpdateAsync updatedUser with
                            | Error error -> return Error error
                            | Ok() -> return Ok(UserResult(updatedUser.State))

            | SetProfilePicture(actorUserId, userId, dto) ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user ->
                        // Update profile picture using domain method with actor (automatically creates event)
                        match User.updateProfilePic actorUserId (Some dto.ProfilePicUrl) user with
                        | Error error -> return Error error
                        | Ok updatedUser ->
                            // Save user and events atomically
                            match! userRepository.UpdateAsync updatedUser with
                            | Error error -> return Error error
                            | Ok() -> return Ok(UserResult(updatedUser.State))

            | RemoveProfilePicture(actorUserId, userId) ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user ->
                        // Remove profile picture using domain method with actor (automatically creates event)
                        let updatedUser = User.removeProfilePicture actorUserId user

                        // Save user and events atomically
                        match! userRepository.UpdateAsync updatedUser with
                        | Error error -> return Error error
                        | Ok() -> return Ok(UserResult(updatedUser.State))

            | GetUserById userId ->
                match Guid.TryParse userId with
                | false, _ -> return Error(ValidationError "Invalid user ID format")
                | true, guid ->
                    let userIdObj = UserId.FromGuid guid
                    let! userOption = userRepository.GetByIdAsync userIdObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user -> return Ok(UserCommandResult.UserResult(user.State))

            | GetUserByEmail email ->
                match Email.Create(Some email) with
                | Error error -> return Error error
                | Ok emailObj ->
                    let! userOption = userRepository.GetByEmailAsync emailObj

                    match userOption with
                    | None -> return Error(NotFound "User not found")
                    | Some user -> return Ok(UserCommandResult.UserResult(user.State))

            | GetAllUsers(skip, take) ->
                if skip < 0 then
                    return Error(ValidationError "Skip cannot be negative")
                elif take <= 0 || take > 100 then
                    return Error(ValidationError "Take must be between 1 and 100")
                else
                    let! users = userRepository.GetAllAsync skip take
                    let! totalCount = userRepository.GetCountAsync()

                    let result = {
                        Items = users |> List.map (fun user -> user.State)
                        TotalCount = totalCount
                        Skip = skip
                        Take = take
                    }

                    return Ok(UserCommandResult.UsersResult result)

            | InviteUser(actorUserId, dto) ->
                // Validate email
                match Email.Create(Some dto.Email) with
                | Error err -> return Error err
                | Ok validEmail ->
                    // Check if email already exists
                    let! existingUser = userRepository.GetByEmailAsync validEmail

                    match existingUser with
                    | Some user when User.isInvitedPlaceholder user ->
                        // Idempotent: return existing placeholder
                        return Ok(UserResult(user.State))
                    | Some _ ->
                        // Real user exists - conflict
                        return Error(Conflict "A user with this email already exists")
                    | None ->
                        // Create new placeholder user
                        let invitedUser = User.invite actorUserId validEmail

                        match! userRepository.AddAsync invitedUser with
                        | Error error -> return Error error
                        | Ok() -> return Ok(UserResult(invitedUser.State))
        }

type UserHandler(userRepository: IUserRepository) =
    interface ICommandHandler<UserCommand, UserCommandResult> with
        member this.HandleCommand command =
            UserHandler.handleCommand userRepository command