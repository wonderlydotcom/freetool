namespace Freetool.Domain.ValueObjects

open Freetool.Domain

type FolderName =
    private
    | FolderName of string

    static member Create(name: string option) : Result<FolderName, DomainError> =
        match name with
        | None
        | Some "" -> Error(ValidationError "Folder name cannot be empty")
        | Some nameValue when nameValue.Length > 100 ->
            Error(ValidationError "Folder name cannot exceed 100 characters")
        | Some nameValue -> Ok(FolderName(nameValue.Trim()))

    member this.Value =
        let (FolderName name) = this
        name

    override this.ToString() = this.Value