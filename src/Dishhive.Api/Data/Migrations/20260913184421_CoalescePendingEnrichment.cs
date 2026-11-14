using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoalescePendingEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "RecipeId", "Task" ORDER BY "RecipeRevision" DESC,
                        "FactsConfirmationVersion" DESC, "UpdatedAt" DESC, "Id") AS position
                    FROM "RecipeEnrichmentJobs" WHERE "State" = 0
                )
                UPDATE "RecipeEnrichmentJobs" SET "State" = 3
                WHERE "Id" IN (SELECT "Id" FROM ranked WHERE position > 1);
                """);
            migrationBuilder.CreateIndex(
                name: "IX_RecipeEnrichmentJobs_RecipeId_Task",
                table: "RecipeEnrichmentJobs",
                columns: new[] { "RecipeId", "Task" },
                unique: true,
                filter: "\"State\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecipeEnrichmentJobs_RecipeId_Task",
                table: "RecipeEnrichmentJobs");
        }
    }
}
