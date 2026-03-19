namespace Freetool.Application.DTOs

open System.Text.Json.Serialization

type FolderLocation =
    | RootFolder
    | ChildFolder of parentId: string

type RadioOptionDto = { Value: string; Label: string option }

[<JsonFSharpConverter(UnionTagName = "case", UnionFieldsName = "fields")>]
type SupportedCurrencyDto = | USD

[<JsonFSharpConverter(UnionTagName = "case", UnionFieldsName = "fields")>]
type InputTypeDto =
    | Email
    | Date
    | Text of MaxLength: int
    | Integer
    | Boolean
    | Currency of Currency: SupportedCurrencyDto
    | MultiEmail of AllowedEmails: string list
    | MultiDate of AllowedDates: string list
    | MultiText of MaxLength: int * AllowedValues: string list
    | MultiInteger of AllowedIntegers: int list
    | Radio of Options: RadioOptionDto list