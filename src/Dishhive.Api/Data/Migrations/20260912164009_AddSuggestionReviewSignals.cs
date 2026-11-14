using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestionReviewSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "MealSuggestionCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewOutcome",
                table: "MealSuggestionCandidates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasModified",
                table: "MealSuggestionCandidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "MealSuggestionCandidates");

            migrationBuilder.DropColumn(
                name: "ReviewOutcome",
                table: "MealSuggestionCandidates");

            migrationBuilder.DropColumn(
                name: "WasModified",
                table: "MealSuggestionCandidates");
        }
    }
}
