using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SumyCRM.Migrations
{
    /// <inheritdoc />
    public partial class voide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Requests");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "Requests",
                newName: "Transcript");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "Requests",
                newName: "Caller");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "Requests",
                newName: "AudioFilePath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Transcript",
                table: "Requests",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "Caller",
                table: "Requests",
                newName: "Message");

            migrationBuilder.RenameColumn(
                name: "AudioFilePath",
                table: "Requests",
                newName: "FullName");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Requests",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
