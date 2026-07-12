using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningIntentDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedIntentJson",
                table: "AiPlanningRuns",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedIntentJson",
                table: "AiPlanningRuns");
        }
    }
}
