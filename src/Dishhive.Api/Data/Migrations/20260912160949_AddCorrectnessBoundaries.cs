using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectnessBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DietaryFactsAssessedRevision",
                table: "Recipes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FactsConfirmationVersion",
                table: "Recipes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Recipes",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            // Existing assessed facts predate recipe revisions. Bind them to the
            // initial revision so they remain current until the ingredients change.
            migrationBuilder.Sql(
                """
                UPDATE "Recipes" AS r
                SET "DietaryFactsAssessedRevision" = r."Revision"
                WHERE r."DietaryFactsStatus" IN (1, 2)
                """);

            migrationBuilder.AlterColumn<string>(
                name: "DishName",
                table: "PlannedMeals",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DishName",
                table: "FamilyMemberFavorites",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputBudgetTokens",
                table: "AiTaskRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PromptHash",
                table: "AiTaskRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "QueueWaitMs",
                table: "AiTaskRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ConfigFingerprint",
                table: "AiPlanningRuns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MealSuggestionBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealSuggestionBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecipeDietaryFactHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Contains = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AssessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeDietaryFactHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeDietaryFactHistory_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeEnrichmentJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeRevision = table.Column<long>(type: "bigint", nullable: false),
                    FactsConfirmationVersion = table.Column<long>(type: "bigint", nullable: false),
                    Task = table.Column<int>(type: "integer", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeEnrichmentJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeEnrichmentJobs_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealSuggestionCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipeRevision = table.Column<long>(type: "bigint", nullable: true),
                    DishName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CandidateVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FreezyItemRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FreezyItemQuantity = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MealType = table.Column<int>(type: "integer", nullable: false),
                    Course = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealSuggestionCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealSuggestionCandidates_MealSuggestionBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "MealSuggestionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealSuggestionBatches_ExpiresAt",
                table: "MealSuggestionBatches",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MealSuggestionCandidates_BatchId_Id",
                table: "MealSuggestionCandidates",
                columns: new[] { "BatchId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeDietaryFactHistory_RecipeId_ArchivedAt",
                table: "RecipeDietaryFactHistory",
                columns: new[] { "RecipeId", "ArchivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeEnrichmentJobs_RecipeId_Task_State",
                table: "RecipeEnrichmentJobs",
                columns: new[] { "RecipeId", "Task", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeEnrichmentJobs_State_NotBefore",
                table: "RecipeEnrichmentJobs",
                columns: new[] { "State", "NotBefore" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealSuggestionCandidates");

            migrationBuilder.DropTable(
                name: "RecipeDietaryFactHistory");

            migrationBuilder.DropTable(
                name: "RecipeEnrichmentJobs");

            migrationBuilder.DropTable(
                name: "MealSuggestionBatches");

            migrationBuilder.DropColumn(
                name: "DietaryFactsAssessedRevision",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "FactsConfirmationVersion",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "InputBudgetTokens",
                table: "AiTaskRuns");

            migrationBuilder.DropColumn(
                name: "PromptHash",
                table: "AiTaskRuns");

            migrationBuilder.DropColumn(
                name: "QueueWaitMs",
                table: "AiTaskRuns");

            migrationBuilder.DropColumn(
                name: "ConfigFingerprint",
                table: "AiPlanningRuns");

            migrationBuilder.AlterColumn<string>(
                name: "DishName",
                table: "PlannedMeals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DishName",
                table: "FamilyMemberFavorites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }
    }
}
