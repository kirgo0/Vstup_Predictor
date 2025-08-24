using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vstup_Predictor.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Scpeciality",
                table: "Offers",
                newName: "Speciality");

            migrationBuilder.AddColumn<string>(
                name: "RequestParameter",
                table: "Universities",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestParameter",
                table: "Offers",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestParameter",
                table: "Cities",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestParameter",
                table: "Applications",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestParameter",
                table: "Universities");

            migrationBuilder.DropColumn(
                name: "RequestParameter",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "RequestParameter",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "RequestParameter",
                table: "Applications");

            migrationBuilder.RenameColumn(
                name: "Speciality",
                table: "Offers",
                newName: "Scpeciality");
        }
    }
}
