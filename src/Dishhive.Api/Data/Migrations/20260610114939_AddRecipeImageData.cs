using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeImageData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageContentType",
                table: "Recipes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ImageData",
                table: "Recipes",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageContentType",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ImageData",
                table: "Recipes");
        }
    }
}
