using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedMealSuggestionKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestionKey",
                table: "PlannedMeals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedMeals_SuggestionKey",
                table: "PlannedMeals",
                column: "SuggestionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannedMeals_SuggestionKey",
                table: "PlannedMeals");

            migrationBuilder.DropColumn(
                name: "SuggestionKey",
                table: "PlannedMeals");
        }
    }
}
