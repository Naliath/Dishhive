using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dishhive.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDietaryFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DietaryFactsAssessedAt",
                table: "Recipes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DietaryFactsStatus",
                table: "Recipes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedClasses",
                table: "FamilyMemberDietaryTags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RecipeDietaryFacts",
                columns: table => new
                {
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientClass = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeDietaryFacts", x => new { x.RecipeId, x.IngredientClass });
                    table.ForeignKey(
                        name: "FK_RecipeDietaryFacts_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Seed preset exclusion classes onto EXISTING member-tag links (new links
            // are seeded by the API via DietaryTagPresets). Snapshot of the preset
            // table at migration time — kept in SQL on purpose, migrations are frozen.
            // Kind: 0 = Allergy, 1 = Diet. Only rows still holding the fresh-column
            // default '' are touched.
            migrationBuilder.Sql("""
                UPDATE "FamilyMemberDietaryTags" AS l
                SET "ExcludedClasses" = m.classes
                FROM "DietaryTags" AS t,
                     (VALUES
                        ('noten', 0, 'TreeNuts'),
                        ('nuts', 0, 'TreeNuts'),
                        ('tree nuts', 0, 'TreeNuts'),
                        ('pinda', 0, 'Peanuts'),
                        ('pinda''s', 0, 'Peanuts'),
                        ('pindas', 0, 'Peanuts'),
                        ('peanut', 0, 'Peanuts'),
                        ('peanuts', 0, 'Peanuts'),
                        ('lactose', 0, 'Milk'),
                        ('melk', 0, 'Milk'),
                        ('milk', 0, 'Milk'),
                        ('zuivel', 0, 'Milk'),
                        ('dairy', 0, 'Milk'),
                        ('gluten', 0, 'Gluten'),
                        ('tarwe', 0, 'Gluten'),
                        ('wheat', 0, 'Gluten'),
                        ('ei', 0, 'Eggs'),
                        ('eieren', 0, 'Eggs'),
                        ('egg', 0, 'Eggs'),
                        ('eggs', 0, 'Eggs'),
                        ('vis', 0, 'Fish'),
                        ('fish', 0, 'Fish'),
                        ('schaaldieren', 0, 'Crustaceans'),
                        ('crustaceans', 0, 'Crustaceans'),
                        ('schelpdieren', 0, 'Molluscs'),
                        ('weekdieren', 0, 'Molluscs'),
                        ('molluscs', 0, 'Molluscs'),
                        ('shellfish', 0, 'Crustaceans,Molluscs'),
                        ('schaal- en schelpdieren', 0, 'Crustaceans,Molluscs'),
                        ('soja', 0, 'Soybeans'),
                        ('soy', 0, 'Soybeans'),
                        ('soya', 0, 'Soybeans'),
                        ('sesam', 0, 'Sesame'),
                        ('sesamzaad', 0, 'Sesame'),
                        ('sesame', 0, 'Sesame'),
                        ('mosterd', 0, 'Mustard'),
                        ('mustard', 0, 'Mustard'),
                        ('selderij', 0, 'Celery'),
                        ('selder', 0, 'Celery'),
                        ('celery', 0, 'Celery'),
                        ('sulfiet', 0, 'Sulphites'),
                        ('sulfieten', 0, 'Sulphites'),
                        ('sulphites', 0, 'Sulphites'),
                        ('sulfites', 0, 'Sulphites'),
                        ('lupine', 0, 'Lupin'),
                        ('lupin', 0, 'Lupin'),
                        ('vegetarisch', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin'),
                        ('vegetariër', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin'),
                        ('vegetarier', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin'),
                        ('vegetarian', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin'),
                        ('veggie', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin'),
                        ('vegan', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin,Milk,Eggs,Honey'),
                        ('veganistisch', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin,Milk,Eggs,Honey'),
                        ('veganist', 1, 'RedMeat,Poultry,Pork,Fish,Crustaceans,Molluscs,Gelatin,Milk,Eggs,Honey'),
                        ('pescotarisch', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('pescetarisch', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('pescatarian', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('pescetarian', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('geen vlees', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('no meat', 1, 'RedMeat,Poultry,Pork,Gelatin'),
                        ('geen rood vlees', 1, 'RedMeat,Pork'),
                        ('no red meat', 1, 'RedMeat,Pork'),
                        ('geen varkensvlees', 1, 'Pork'),
                        ('geen varken', 1, 'Pork'),
                        ('no pork', 1, 'Pork'),
                        ('varkensvrij', 1, 'Pork'),
                        ('halal', 1, 'Pork,Alcohol,Gelatin'),
                        ('geen alcohol', 1, 'Alcohol'),
                        ('no alcohol', 1, 'Alcohol'),
                        ('alcoholvrij', 1, 'Alcohol')
                     ) AS m(name, kind, classes)
                WHERE t."Id" = l."DietaryTagId"
                  AND t."Kind" = m.kind
                  AND lower(t."Name") = m.name
                  AND l."ExcludedClasses" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeDietaryFacts");

            migrationBuilder.DropColumn(
                name: "DietaryFactsAssessedAt",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "DietaryFactsStatus",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ExcludedClasses",
                table: "FamilyMemberDietaryTags");
        }
    }
}
