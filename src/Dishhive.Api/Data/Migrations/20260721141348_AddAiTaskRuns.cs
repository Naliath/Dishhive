using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTaskRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiTaskRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Task = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigFingerprint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContractVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResponseMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FinishReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    Temperature = table.Column<double>(type: "double precision", nullable: false),
                    ReasoningDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    SchemaRequested = table.Column<bool>(type: "boolean", nullable: false),
                    SchemaApplied = table.Column<bool>(type: "boolean", nullable: false),
                    PromptCharacters = table.Column<long>(type: "bigint", nullable: false),
                    OutputCharacters = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ReasoningTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTaskRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskRuns_Outcome",
                table: "AiTaskRuns",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskRuns_StartedAt",
                table: "AiTaskRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskRuns_Task",
                table: "AiTaskRuns",
                column: "Task");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiTaskRuns");
        }
    }
}
