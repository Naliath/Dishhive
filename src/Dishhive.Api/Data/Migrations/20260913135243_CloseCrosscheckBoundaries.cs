using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CloseCrosscheckBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FactsConfirmationVersion",
                table: "MealSuggestionCandidates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewSnapshotJson",
                table: "MealSuggestionCandidates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FactsConfirmationVersion",
                table: "MealSuggestionCandidates");

            migrationBuilder.DropColumn(
                name: "ReviewSnapshotJson",
                table: "MealSuggestionCandidates");
        }
    }
}
