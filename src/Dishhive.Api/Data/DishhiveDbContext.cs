using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Data;

public class DishhiveDbContext : DbContext
{
    public DishhiveDbContext(DbContextOptions<DishhiveDbContext> options) : base(options)
    {
    }

    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<MemberPreference> MemberPreferences => Set<MemberPreference>();
    public DbSet<FavoriteDish> FavoriteDishes => Set<FavoriteDish>();
    public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
    public DbSet<PlannedMeal> PlannedMeals => Set<PlannedMeal>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<DishRating> DishRatings => Set<DishRating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Recipe configuration
        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.PictureUrl).HasColumnType("text");
            entity.Property(e => e.VideoUrl).HasMaxLength(2000);
            entity.Property(e => e.SourceUrl).HasMaxLength(2000);
            entity.Property(e => e.SourceName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Tags stored as JSON array
            entity.Property(e => e.Tags)
                  .HasColumnType("jsonb")
                  .HasDefaultValueSql("'[]'::jsonb");

            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.SourceUrl);
        });

        // RecipeIngredient configuration
        modelBuilder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.OriginalUnit).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(500);

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

        // FamilyMember configuration
        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // MemberPreference configuration
        modelBuilder.Entity<MemberPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Value).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.FamilyMember)
                  .WithMany(m => m.Preferences)
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FamilyMemberId);
        });

        // FavoriteDish configuration
        modelBuilder.Entity<FavoriteDish>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.DishName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.FamilyMember)
                  .WithMany(m => m.FavoriteDishes)
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FamilyMemberId);
        });

        // WeekPlan configuration
        modelBuilder.Entity<WeekPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // One plan per week
            entity.HasIndex(e => e.WeekStartDate).IsUnique();
        });

        // PlannedMeal configuration
        modelBuilder.Entity<PlannedMeal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.VagueInstruction).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // AttendeeIds stored as JSON array
            entity.Property(e => e.AttendeeIds)
                  .HasColumnType("jsonb")
                  .HasDefaultValueSql("'[]'::jsonb");

            entity.HasOne(e => e.WeekPlan)
                  .WithMany(w => w.Meals)
                  .HasForeignKey(e => e.WeekPlanId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.WeekPlanId);
            entity.HasIndex(e => e.RecipeId);
        });

        // UserSetting configuration
        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired().HasMaxLength(500);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.Key).IsUnique();
        });

        // DishRating configuration
        modelBuilder.Entity<DishRating>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Comment).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Recipe)
                  .WithMany()
                  .HasForeignKey(e => e.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FamilyMember)
                  .WithMany()
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.RecipeId);
            entity.HasIndex(e => e.RatedOn);
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
            if (entry.Properties.Any(p => p.Metadata.Name == "UpdatedAt"))
            {
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
            }
        }
    }
}
