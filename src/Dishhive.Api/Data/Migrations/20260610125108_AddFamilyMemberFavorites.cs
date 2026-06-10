using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyMemberFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DishName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMemberFavorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMemberFavorites_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyMemberFavorites_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberFavorites_FamilyMemberId",
                table: "FamilyMemberFavorites",
                column: "FamilyMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberFavorites_RecipeId",
                table: "FamilyMemberFavorites",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyMemberFavorites");
        }
    }
}
