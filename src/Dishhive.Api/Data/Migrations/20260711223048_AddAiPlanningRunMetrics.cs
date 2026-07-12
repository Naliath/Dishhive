using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPlanningRunMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiPlanningRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    RequestId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedDays = table.Column<int>(type: "integer", nullable: false),
                    SuggestedItems = table.Column<int>(type: "integer", nullable: false),
                    ExternalSuggestions = table.Column<int>(type: "integer", nullable: false),
                    FallbackSuggestions = table.Column<int>(type: "integer", nullable: false),
                    UsedExternalResearch = table.Column<bool>(type: "boolean", nullable: false),
                    CompletionAttempts = table.Column<int>(type: "integer", nullable: false),
                    ParseFailures = table.Column<int>(type: "integer", nullable: false),
                    ModelTurns = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ReasoningTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    ResearchCalls = table.Column<int>(type: "integer", nullable: false),
                    SearchCount = table.Column<int>(type: "integer", nullable: false),
                    EmptySearchCount = table.Column<int>(type: "integer", nullable: false),
                    SearchResultCount = table.Column<int>(type: "integer", nullable: false),
                    RecipeResolutionCount = table.Column<int>(type: "integer", nullable: false),
                    RecipeResolutionFailureCount = table.Column<int>(type: "integer", nullable: false),
                    CapabilityWaitMs = table.Column<long>(type: "bigint", nullable: false),
                    CompletionDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    SearchDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    RecipeResolutionDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPlanningRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPlanningRuns_Outcome",
                table: "AiPlanningRuns",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_AiPlanningRuns_StartedAt",
                table: "AiPlanningRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPlanningRuns");
        }
    }
}
