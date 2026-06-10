using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedMealCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannedMeals_Date_MealType",
                table: "PlannedMeals");

            migrationBuilder.AddColumn<int>(
                name: "Course",
                table: "PlannedMeals",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Course",
                table: "PlannedMeals");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedMeals_Date_MealType",
                table: "PlannedMeals",
                columns: new[] { "Date", "MealType" },
                unique: true);
        }
    }
}
