using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SumyCRM.Migrations
{
    /// <inheritdoc />
    public partial class text : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Transcript",
                table: "Requests",
                newName: "Text");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Requests",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Requests");

            migrationBuilder.RenameColumn(
                name: "Text",
                table: "Requests",
                newName: "Transcript");
        }
    }
}
