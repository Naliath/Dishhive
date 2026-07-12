using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeLocalizationOriginals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalInstruction",
                table: "RecipeSteps",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentLanguage",
                table: "Recipes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalDescription",
                table: "Recipes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalTitle",
                table: "Recipes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalInstruction",
                table: "RecipeSteps");

            migrationBuilder.DropColumn(
                name: "ContentLanguage",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "OriginalDescription",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "OriginalTitle",
                table: "Recipes");
        }
    }
}
