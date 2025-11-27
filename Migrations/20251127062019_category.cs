using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SumyCRM.Migrations
{
    /// <inheritdoc />
    public partial class category : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Requests",
                newName: "DateAdded");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Requests",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<bool>(
                name: "Hidden",
                table: "Requests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "94c19b57-7312-4a4d-9a14-98f5a123269a",
                columns: new[] { "ConcurrencyStamp", "PasswordHash" },
                values: new object[] { "bb394c95-62b5-41ac-9c74-edc72dcb7480", "AQAAAAIAAYagAAAAEJLhnIyRa/i0GPn8/3rM+AjxUhtX5oesoTStsKcmi/bOZwHmUQ8u81dxdwRm9yB9NA==" });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "DateAdded", "Hidden", "Title" },
                values: new object[] { new Guid("5ef8838e-3264-4277-9604-92b004d97224"), new DateTime(2025, 11, 27, 6, 20, 19, 164, DateTimeKind.Utc).AddTicks(599), false, "Вода" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CategoryId",
                table: "Requests",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Categories_CategoryId",
                table: "Requests",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Categories_CategoryId",
                table: "Requests");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CategoryId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Hidden",
                table: "Requests");

            migrationBuilder.RenameColumn(
                name: "DateAdded",
                table: "Requests",
                newName: "CreatedAt");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "94c19b57-7312-4a4d-9a14-98f5a123269a",
                columns: new[] { "ConcurrencyStamp", "PasswordHash" },
                values: new object[] { "b5d0dc27-ec94-45bf-8559-add94e09dfa3", "AQAAAAIAAYagAAAAEGmiVpy6CaCQGNAV3Pav+T/sFvHFdcw+pSUztIvRmHUM1fSwXrPzfQraTIZKABX0Yg==" });
        }
    }
}
