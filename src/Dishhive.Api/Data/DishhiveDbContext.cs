using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Data;

public class DishhiveDbContext : DbContext
{
    public DishhiveDbContext(DbContextOptions<DishhiveDbContext> options) : base(options)
    {
    }

    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyMemberFavorite> FamilyMemberFavorites => Set<FamilyMemberFavorite>();
    public DbSet<DietaryTag> DietaryTags => Set<DietaryTag>();
    public DbSet<FamilyMemberDietaryTag> FamilyMemberDietaryTags => Set<FamilyMemberDietaryTag>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();
    public DbSet<RecipeTagAssignment> RecipeTagAssignments => Set<RecipeTagAssignment>();
    public DbSet<Cookbook> Cookbooks => Set<Cookbook>();
    public DbSet<PlannedMeal> PlannedMeals => Set<PlannedMeal>();
    public DbSet<PlannedMealAttendee> PlannedMealAttendees => Set<PlannedMealAttendee>();
    public DbSet<MealRating> MealRatings => Set<MealRating>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // FamilyMember configuration
        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PreferenceNotes).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Index for the common "active members" listing
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsGuest);
        });

        // FamilyMemberFavorite configuration
        modelBuilder.Entity<FamilyMemberFavorite>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.DishName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.FamilyMember)
                  .WithMany()
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Recipe)
                  .WithMany()
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.FamilyMemberId);
        });

        // DietaryTag configuration
        modelBuilder.Entity<DietaryTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // One tag per name per kind; lookups are case-insensitive in the API
            entity.HasIndex(e => new { e.Name, e.Kind }).IsUnique();
        });

        // FamilyMemberDietaryTag configuration (composite key join table)
        modelBuilder.Entity<FamilyMemberDietaryTag>(entity =>
        {
            entity.HasKey(e => new { e.FamilyMemberId, e.DietaryTagId });

            entity.HasOne(e => e.FamilyMember)
                  .WithMany(m => m.DietaryTags)
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DietaryTag)
                  .WithMany(t => t.Members)
                  .HasForeignKey(e => e.DietaryTagId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Recipe configuration
        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Servings).HasDefaultValue(4);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Keywords).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(1000);
            entity.Property(e => e.ImageContentType).HasMaxLength(100);
            entity.Property(e => e.VideoUrl).HasMaxLength(1000);
            entity.Property(e => e.SourceUrl).HasMaxLength(1000);
            entity.Property(e => e.SourceProvider).HasMaxLength(100);
            entity.Property(e => e.SourceRawData).HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Index for title search
            entity.HasIndex(e => e.Title);

            // Unique source URL prevents duplicate imports (re-import updates instead)
            entity.HasIndex(e => e.SourceUrl).IsUnique();
        });

        // RecipeIngredient configuration
        modelBuilder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.OriginalText).IsRequired().HasMaxLength(300);
            entity.Property(e => e.OriginalUnit).HasMaxLength(50);

            entity.HasOne(e => e.Recipe)
                  .WithMany(r => r.Ingredients)
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.RecipeId);
        });

        // RecipeStep configuration
        modelBuilder.Entity<RecipeStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Instruction).IsRequired().HasMaxLength(2000);

            entity.HasOne(e => e.Recipe)
                  .WithMany(r => r.Steps)
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.RecipeId);
        });

        // RecipeTag configuration (same pattern as DietaryTag)
        modelBuilder.Entity<RecipeTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // One tag per name; lookups are case-insensitive in the API
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // RecipeTagAssignment configuration (composite key join table)
        modelBuilder.Entity<RecipeTagAssignment>(entity =>
        {
            entity.HasKey(e => new { e.RecipeId, e.RecipeTagId });

            entity.HasOne(e => e.Recipe)
                  .WithMany(r => r.Tags)
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecipeTag)
                  .WithMany(t => t.Recipes)
                  .HasForeignKey(e => e.RecipeTagId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Cookbook configuration (saved filters; tags stored by name, no FK)
        modelBuilder.Entity<Cookbook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SearchTerm).HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // PlannedMeal configuration
        modelBuilder.Entity<PlannedMeal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.DishName).HasMaxLength(200);
            entity.Property(e => e.VagueInstruction).HasMaxLength(500);
            entity.Property(e => e.FreezyItemRef).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Recipe)
                  .WithMany()
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Indexes for history/statistics queries; a day can hold any number
            // of dishes (e.g. lunch plus a dinner with appetizer and dessert)
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.DishName);
        });

        // PlannedMealAttendee configuration (composite key join table)
        modelBuilder.Entity<PlannedMealAttendee>(entity =>
        {
            entity.HasKey(e => new { e.PlannedMealId, e.FamilyMemberId });

            entity.HasOne(e => e.PlannedMeal)
                  .WithMany(m => m.Attendees)
                  .HasForeignKey(e => e.PlannedMealId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FamilyMember)
                  .WithMany()
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // MealRating configuration (composite key join table, like PlannedMealAttendee)
        modelBuilder.Entity<MealRating>(entity =>
        {
            entity.HasKey(e => new { e.PlannedMealId, e.FamilyMemberId });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.PlannedMeal)
                  .WithMany(m => m.Ratings)
                  .HasForeignKey(e => e.PlannedMealId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FamilyMember)
                  .WithMany()
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);

            // For per-member rating statistics
            entity.HasIndex(e => e.FamilyMemberId);
        });

        // UserSetting configuration
        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case FamilyMember member:
                    member.UpdatedAt = DateTime.UtcNow;
                    break;
                case Recipe recipe:
                    recipe.UpdatedAt = DateTime.UtcNow;
                    break;
                case PlannedMeal meal:
                    meal.UpdatedAt = DateTime.UtcNow;
                    break;
                case MealRating rating:
                    rating.UpdatedAt = DateTime.UtcNow;
                    break;
                case UserSetting setting:
                    setting.UpdatedAt = DateTime.UtcNow;
                    break;
                case Cookbook cookbook:
                    cookbook.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }
    }
}
