namespace Dishhive.Infrastructure.Persistence;

using Dishhive.Domain.Common;
using Dishhive.Domain.Entities.Family;
using Dishhive.Domain.Entities.Planner;
using Dishhive.Domain.Entities.Recipes;
using Dishhive.Domain.Entities.ShoppingList;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Main DbContext for Dishhive application.
/// </summary>
public class DishhiveDbContext : DbContext
{
    public DishhiveDbContext(DbContextOptions<DishhiveDbContext> options) : base(options)
    {
    }

    // Family
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Allergy> Allergies => Set<Allergy>();
    public DbSet<DietaryPreference> DietaryPreferences => Set<DietaryPreference>();

    // Recipes
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<PreparationStep> PreparationSteps => Set<PreparationStep>();
    public DbSet<Tag> Tags => Set<Tag>();

    // Planner
    public DbSet<MealSlot> MealSlots => Set<MealSlot>();

    // Shopping List
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplyConfigurations(modelBuilder);
    }

    private static void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        // Configure all entities to use GUID keys
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var keyProperty = entity.FindProperty("Id");
            if (keyProperty != null && keyProperty.PropertyInfo?.DeclaringType?.Namespace?.StartsWith("Dishhive.Domain") == true)
            {
                // Ensure GUID keys are configured
            }
        }

        // FamilyMember configurations
        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasMany(fm => fm.Allergies)
                .WithOne()
                .HasForeignKey("FamilyMemberId");

            entity.HasMany(fm => fm.DietaryPreferences)
                .WithOne()
                .HasForeignKey("FamilyMemberId");
        });

        // Recipe configurations
        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.HasMany(r => r.Ingredients)
                .WithOne()
                .HasForeignKey("RecipeId");

            entity.HasMany(r => r.PreparationSteps)
                .WithOne()
                .HasForeignKey("RecipeId");

            entity.HasMany(r => r.Tags)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "RecipeTag",
                    j => j.HasOne<Tag>().WithMany(),
                    j => j.HasOne<Recipe>().WithMany());
        });

        //

        // ShoppingList configurations
        modelBuilder.Entity<ShoppingList>(entity =>
        {
            entity.HasMany(sl => sl.Items)
                .WithOne()
                .HasForeignKey("ShoppingListId");
        });
    }
}
