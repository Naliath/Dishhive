using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordAiReviewAndInputRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "MealSuggestionBatches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputFingerprint",
                table: "AiTaskRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputRevisionsJson",
                table: "AiTaskRuns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiReviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Task = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuggestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: true),
                    ObservedRevision = table.Column<long>(type: "bigint", nullable: true),
                    SourceConfirmationVersion = table.Column<long>(type: "bigint", nullable: true),
                    ObservedConfirmationVersion = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ChangedFieldsJson = table.Column<string>(type: "text", nullable: false),
                    InputChangesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiReviewEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiReviewEvents_OccurredAt",
                table: "AiReviewEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiReviewEvents_OperationId",
                table: "AiReviewEvents",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiReviewEvents_RecipeId",
                table: "AiReviewEvents",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiReviewEvents");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "MealSuggestionBatches");

            migrationBuilder.DropColumn(
                name: "InputFingerprint",
                table: "AiTaskRuns");

            migrationBuilder.DropColumn(
                name: "InputRevisionsJson",
                table: "AiTaskRuns");
        }
    }
}
