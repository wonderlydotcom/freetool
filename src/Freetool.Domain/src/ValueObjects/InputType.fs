namespace Freetool.Domain.ValueObjects

open Freetool.Domain
open System

/// Represents a single option in a radio button group
type RadioOption = { Value: string; Label: string option }

/// Supported currency literals for currency input types
type SupportedCurrency = | USD

type InputTypeValue =
    | Email
    | Date
    | Text of maxLength: int
    | Integer
    | Boolean
    | Currency of currency: SupportedCurrency
    | MultiEmail of allowedEmails: Email list
    | MultiDate of allowedDates: DateTime list
    | MultiText of maxLength: int * allowedValues: string list
    | MultiInteger of allowedIntegers: int list
    | Radio of options: RadioOption list

[<Struct>]
type InputType =
    | InputType of InputTypeValue

    static member Email() = InputType(Email)
    static member Date() = InputType(Date)

    static member Text(maxLength: int) =
        if maxLength <= 0 || maxLength > 500 then
            Error(ValidationError "Text input max length must be between 1 and 500 characters")
        else
            Ok(InputType(Text(maxLength)))

    static member Integer() = InputType(Integer)
    static member Boolean() = InputType(Boolean)
    static member Currency(currency: SupportedCurrency) = InputType(Currency currency)

    static member MultiEmail(allowedEmails: Email list) : Result<InputType, DomainError> =
        if List.isEmpty allowedEmails then
            Error(ValidationError "MultiEmail must have at least one allowed email")
        else
            Ok(InputType(MultiEmail(allowedEmails)))

    static member MultiDate(allowedDates: DateTime list) : Result<InputType, DomainError> =
        if List.isEmpty allowedDates then
            Error(ValidationError "MultiDate must have at least one allowed date")
        else
            Ok(InputType(MultiDate(allowedDates)))

    static member MultiText(maxLength: int, allowedValues: string list) : Result<InputType, DomainError> =
        if maxLength <= 0 || maxLength > 500 then
            Error(ValidationError "MultiText max length must be between 1 and 500 characters")
        elif List.isEmpty allowedValues then
            Error(ValidationError "MultiText must have at least one allowed value")
        elif allowedValues |> List.exists (fun v -> v.Length > maxLength) then
            Error(ValidationError "All MultiText allowed values must not exceed max length")
        else
            Ok(InputType(MultiText(maxLength, allowedValues)))

    static member MultiInteger(allowedIntegers: int list) : Result<InputType, DomainError> =
        if List.isEmpty allowedIntegers then
            Error(ValidationError "MultiInteger must have at least one allowed integer")
        else
            Ok(InputType(MultiInteger(allowedIntegers)))

    static member Radio(options: RadioOption list) : Result<InputType, DomainError> =
        if List.length options < 2 then
            Error(ValidationError "Radio must have at least 2 options")
        elif List.length options > 50 then
            Error(ValidationError "Radio cannot have more than 50 options")
        elif options |> List.exists (fun o -> String.IsNullOrWhiteSpace(o.Value)) then
            Error(ValidationError "Radio option values cannot be empty or whitespace")
        else
            let values = options |> List.map (fun o -> o.Value)
            let uniqueValues = values |> Set.ofList

            if List.length values <> Set.count uniqueValues then
                Error(ValidationError "Radio option values must be unique")
            elif options |> List.exists (fun o -> o.Value.Length > 100) then
                Error(ValidationError "Radio option values cannot exceed 100 characters")
            elif
                options
                |> List.exists (fun o ->
                    match o.Label with
                    | Some label -> label.Length > 100
                    | None -> false)
            then
                Error(ValidationError "Radio option labels cannot exceed 100 characters")
            else
                Ok(InputType(Radio(options)))

    member this.Value =
        let (InputType inputType) = this
        inputType

    override this.ToString() =
        match this.Value with
        | Email -> "Email"
        | Date -> "Date"
        | Text maxLength -> sprintf "Text(%d)" maxLength
        | Integer -> "Integer"
        | Boolean -> "Boolean"
        | Currency currency ->
            let currencyCode =
                match currency with
                | USD -> "USD"

            sprintf "Currency(%s)" currencyCode
        | MultiEmail allowedEmails ->
            let emailStrings = allowedEmails |> List.map (fun e -> e.ToString())
            sprintf "MultiEmail([%s])" (String.Join(", ", emailStrings))
        | MultiDate allowedDates ->
            let dateStrings = allowedDates |> List.map (fun d -> d.ToString("yyyy-MM-dd"))
            sprintf "MultiDate([%s])" (String.Join(", ", dateStrings))
        | MultiText(maxLength, allowedValues) ->
            sprintf "MultiText(%d, [%s])" maxLength (String.Join(", ", allowedValues))
        | MultiInteger allowedIntegers ->
            let intStrings = allowedIntegers |> List.map string
            sprintf "MultiInteger([%s])" (String.Join(", ", intStrings))
        | Radio options ->
            let optionStrings =
                options
                |> List.map (fun o ->
                    match o.Label with
                    | Some label -> sprintf "\"%s\":\"%s\"" o.Value label
                    | None -> sprintf "\"%s\"" o.Value)

            sprintf "Radio([%s])" (String.Join(", ", optionStrings))