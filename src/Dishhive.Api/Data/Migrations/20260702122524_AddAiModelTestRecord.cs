using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelTestRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelTestRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ConfigKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndpointReachable = table.Column<bool>(type: "boolean", nullable: false),
                    ModelListed = table.Column<bool>(type: "boolean", nullable: true),
                    ResponseMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EvaluationPassed = table.Column<bool>(type: "boolean", nullable: true),
                    ChecksJson = table.Column<string>(type: "jsonb", nullable: false),
                    TokensPerSecond = table.Column<double>(type: "double precision", nullable: true),
                    ElapsedMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelTestRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelTestRecords_ConfigKey",
                table: "AiModelTestRecords",
                column: "ConfigKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiModelTestRecords");
        }
    }
}
