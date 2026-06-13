using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cookbooks change meaning here: from saved filters to explicitly curated
            // collections. The old filter rows are not convertible and are dropped
            // (decided June 2026; see docs/features/recipe-organization.md).
            migrationBuilder.Sql("""DELETE FROM "Cookbooks";""");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "SearchTerm",
                table: "Cookbooks");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Cookbooks");

            migrationBuilder.CreateTable(
                name: "CookbookEntries",
                columns: table => new
                {
                    CookbookId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookbookEntries", x => new { x.CookbookId, x.RecipeId });
                    table.ForeignKey(
                        name: "FK_CookbookEntries_Cookbooks_CookbookId",
                        column: x => x.CookbookId,
                        principalTable: "Cookbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CookbookEntries_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CookbookEntries_RecipeId",
                table: "CookbookEntries",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookbookEntries");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Cookbooks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchTerm",
                table: "Cookbooks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "Cookbooks",
                type: "text[]",
                nullable: false);
        }
    }
}
