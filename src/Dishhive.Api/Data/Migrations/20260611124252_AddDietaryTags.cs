using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <summary>
    /// Replaces the free-text FamilyMembers.Allergies/DietaryConstraints columns with
    /// structured dietary tags. Existing values are converted before the columns are
    /// dropped: comma-separated entries become individual tags (deduplicated
    /// case-insensitively across members), linked through FamilyMemberDietaryTags.
    /// </summary>
    public partial class AddDietaryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DietaryTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietaryTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMemberDietaryTags",
                columns: table => new
                {
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    DietaryTagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMemberDietaryTags", x => new { x.FamilyMemberId, x.DietaryTagId });
                    table.ForeignKey(
                        name: "FK_FamilyMemberDietaryTags_DietaryTags_DietaryTagId",
                        column: x => x.DietaryTagId,
                        principalTable: "DietaryTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyMemberDietaryTags_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DietaryTags_Name_Kind",
                table: "DietaryTags",
                columns: new[] { "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberDietaryTags_DietaryTagId",
                table: "FamilyMemberDietaryTags",
                column: "DietaryTagId");

            // Convert existing free text (comma-separated, e.g. "nuts, lactose") into
            // tags before the columns are dropped. Kind 0 = Allergy, 1 = Diet.
            ConvertColumnToTags(migrationBuilder, "Allergies", kind: 0);
            ConvertColumnToTags(migrationBuilder, "DietaryConstraints", kind: 1);

            migrationBuilder.DropColumn(
                name: "Allergies",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "DietaryConstraints",
                table: "FamilyMembers");
        }

        private static void ConvertColumnToTags(MigrationBuilder migrationBuilder, string column, int kind)
        {
            // One tag per distinct value (case-insensitive), capped at the new 50-char limit
            migrationBuilder.Sql($"""
                INSERT INTO "DietaryTags" ("Name", "Kind")
                SELECT DISTINCT ON (lower(left(btrim(part), 50))) left(btrim(part), 50), {kind}
                FROM "FamilyMembers", unnest(string_to_array("{column}", ',')) AS part
                WHERE "{column}" IS NOT NULL AND btrim(part) <> '';
                """);

            migrationBuilder.Sql($"""
                INSERT INTO "FamilyMemberDietaryTags" ("FamilyMemberId", "DietaryTagId")
                SELECT DISTINCT m."Id", t."Id"
                FROM "FamilyMembers" m
                CROSS JOIN LATERAL unnest(string_to_array(m."{column}", ',')) AS part
                JOIN "DietaryTags" t
                  ON t."Kind" = {kind} AND lower(t."Name") = lower(left(btrim(part), 50))
                WHERE m."{column}" IS NOT NULL AND btrim(part) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Allergies",
                table: "FamilyMembers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DietaryConstraints",
                table: "FamilyMembers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Restore the free-text columns from the tags before dropping the tables
            RestoreColumnFromTags(migrationBuilder, "Allergies", kind: 0);
            RestoreColumnFromTags(migrationBuilder, "DietaryConstraints", kind: 1);

            migrationBuilder.DropTable(
                name: "FamilyMemberDietaryTags");

            migrationBuilder.DropTable(
                name: "DietaryTags");
        }

        private static void RestoreColumnFromTags(MigrationBuilder migrationBuilder, string column, int kind)
        {
            migrationBuilder.Sql($"""
                UPDATE "FamilyMembers" m
                SET "{column}" = sub.names
                FROM (
                    SELECT l."FamilyMemberId" AS member_id,
                           left(string_agg(t."Name", ', ' ORDER BY t."Name"), 500) AS names
                    FROM "FamilyMemberDietaryTags" l
                    JOIN "DietaryTags" t ON t."Id" = l."DietaryTagId"
                    WHERE t."Kind" = {kind}
                    GROUP BY l."FamilyMemberId"
                ) sub
                WHERE m."Id" = sub.member_id;
                """);
        }
    }
}
