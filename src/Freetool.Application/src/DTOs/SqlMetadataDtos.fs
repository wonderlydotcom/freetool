namespace Freetool.Application.DTOs

type SqlTableInfoDto = { Name: string; Schema: string }

type SqlColumnInfoDto = {
    Name: string
    DataType: string
    IsNullable: bool
}