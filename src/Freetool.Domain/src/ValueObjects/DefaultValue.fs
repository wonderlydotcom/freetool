namespace Freetool.Domain.ValueObjects

open System
open System.Globalization
open Freetool.Domain

/// Typed default value that corresponds to an InputType
type DefaultValueType =
    | EmailDefault of Email
    | DateDefault of DateTime
    | TextDefault of text: string
    | IntegerDefault of int
    | BooleanDefault of bool
    | CurrencyDefault of currency: SupportedCurrency * amount: decimal
    | RadioDefault of selectedValue: string

[<Struct>]
type DefaultValue =
    | DefaultValue of DefaultValueType

    static member private HasAtMostTwoDecimalPlaces(value: decimal) =
        let bits = Decimal.GetBits(value)
        let scale = (bits.[3] >>> 16) &&& 0xFF
        scale <= 2

    /// Create a default value validated against an input type
    static member Create(inputType: InputType, rawValue: string) : Result<DefaultValue, DomainError> =
        match inputType.Value with
        | InputTypeValue.Email -> Email.Create(Some rawValue) |> Result.map (EmailDefault >> DefaultValue)
        | InputTypeValue.Date ->
            match DateTime.TryParse(rawValue) with
            | true, date -> Ok(DefaultValue(DateDefault date))
            | false, _ -> Error(ValidationError "Default value must be a valid date")
        | InputTypeValue.Text maxLength ->
            if rawValue.Length > maxLength then
                Error(ValidationError $"Default value exceeds max length of {maxLength}")
            else
                Ok(DefaultValue(TextDefault rawValue))
        | InputTypeValue.Integer ->
            match Int32.TryParse(rawValue) with
            | true, i -> Ok(DefaultValue(IntegerDefault i))
            | false, _ -> Error(ValidationError "Default value must be a valid integer")
        | InputTypeValue.Boolean ->
            match Boolean.TryParse(rawValue) with
            | true, b -> Ok(DefaultValue(BooleanDefault b))
            | false, _ -> Error(ValidationError "Default value must be 'true' or 'false'")
        | InputTypeValue.Currency currency ->
            match Decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture) with
            | false, _ -> Error(ValidationError "Default value must be a valid currency amount")
            | true, amount when amount < 0m -> Error(ValidationError "Default value must be greater than or equal to 0")
            | true, amount when not (DefaultValue.HasAtMostTwoDecimalPlaces amount) ->
                Error(ValidationError "Default value must have at most 2 decimal places")
            | true, amount -> Ok(DefaultValue(CurrencyDefault(currency, amount)))
        | InputTypeValue.Radio options ->
            if options |> List.exists (fun o -> o.Value = rawValue) then
                Ok(DefaultValue(RadioDefault rawValue))
            else
                Error(ValidationError "Default value must be one of the radio options")
        | InputTypeValue.MultiEmail allowedEmails ->
            match Email.Create(Some rawValue) with
            | Error e -> Error e
            | Ok email ->
                if allowedEmails |> List.exists (fun e -> e.ToString() = email.ToString()) then
                    Ok(DefaultValue(EmailDefault email))
                else
                    Error(ValidationError "Default value must be one of the allowed emails")
        | InputTypeValue.MultiDate allowedDates ->
            match DateTime.TryParse(rawValue) with
            | false, _ -> Error(ValidationError "Default value must be a valid date")
            | true, date ->
                if allowedDates |> List.exists (fun d -> d.Date = date.Date) then
                    Ok(DefaultValue(DateDefault date))
                else
                    Error(ValidationError "Default value must be one of the allowed dates")
        | InputTypeValue.MultiText(maxLength, allowedValues) ->
            if rawValue.Length > maxLength then
                Error(ValidationError $"Default value exceeds max length of {maxLength}")
            elif allowedValues |> List.contains rawValue then
                Ok(DefaultValue(TextDefault rawValue))
            else
                Error(ValidationError "Default value must be one of the allowed values")
        | InputTypeValue.MultiInteger allowedIntegers ->
            match Int32.TryParse(rawValue) with
            | false, _ -> Error(ValidationError "Default value must be a valid integer")
            | true, i ->
                if allowedIntegers |> List.contains i then
                    Ok(DefaultValue(IntegerDefault i))
                else
                    Error(ValidationError "Default value must be one of the allowed integers")

    member this.Value =
        let (DefaultValue v) = this
        v

    /// Convert typed default value back to string for runtime use
    member this.ToRawString() =
        match this.Value with
        | EmailDefault email -> email.ToString()
        | DateDefault date -> date.ToString("O")
        | TextDefault text -> text
        | IntegerDefault i -> string i
        | BooleanDefault b -> if b then "true" else "false"
        | CurrencyDefault(_, amount) -> amount.ToString("0.00", CultureInfo.InvariantCulture)
        | RadioDefault value -> value