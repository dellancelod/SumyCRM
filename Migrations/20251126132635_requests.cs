using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SumyCRM.Migrations
{
    /// <inheritdoc />
    public partial class requests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Requests",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "Requests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RequestNumber",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "94c19b57-7312-4a4d-9a14-98f5a123269a",
                columns: new[] { "ConcurrencyStamp", "PasswordHash" },
                values: new object[] { "b5d0dc27-ec94-45bf-8559-add94e09dfa3", "AQAAAAIAAYagAAAAEGmiVpy6CaCQGNAV3Pav+T/sFvHFdcw+pSUztIvRmHUM1fSwXrPzfQraTIZKABX0Yg==" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                table: "Requests");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Requests",
                type: "int",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "94c19b57-7312-4a4d-9a14-98f5a123269a",
                columns: new[] { "ConcurrencyStamp", "PasswordHash" },
                values: new object[] { "a97641d9-4659-45a4-b4cd-a5816c218c3f", "AQAAAAIAAYagAAAAEP+eIZIbsxKAs3V3W6G42KaaDDkZjw5CBrpfJ7rNvxfIY//m1yV8ofpMMD9B+ISPyA==" });
        }
    }
}
