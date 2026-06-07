using Dishhive.Api.Models.Agents;
using Dishhive.Api.Models.Family;
using Dishhive.Api.Models.History;
using Dishhive.Api.Models.Planning;
using Dishhive.Api.Models.Recipes;
using Dishhive.Api.Models.Settings;
using Dishhive.Api.Models.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Data;

public class DishhiveDbContext : DbContext
{
    public DishhiveDbContext(DbContextOptions<DishhiveDbContext> options) : base(options)
    {
    }

    // Family
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyMemberPreference> FamilyMemberPreferences => Set<FamilyMemberPreference>();
    public DbSet<Guest> Guests => Set<Guest>();

    // Recipes
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();

    // Planning
    public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
    public DbSet<MealSlot> MealSlots => Set<MealSlot>();
    public DbSet<MealSlotAttendee> MealSlotAttendees => Set<MealSlotAttendee>();

    // History
    public DbSet<DishHistoryEntry> DishHistory => Set<DishHistoryEntry>();
    public DbSet<DishFavorite> DishFavorites => Set<DishFavorite>();

    // Shopping
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    // Settings
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    // Agents (learned recipe sources)
    public DbSet<LearnedRecipeSource> LearnedRecipeSources => Set<LearnedRecipeSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ----- Family ---------------------------------------------------------
        modelBuilder.Entity<FamilyMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasMany(x => x.Preferences)
                .WithOne(p => p.FamilyMember)
                .HasForeignKey(p => p.FamilyMemberId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FamilyMemberPreference>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.FamilyMemberId);
        });

        modelBuilder.Entity<Guest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // ----- Recipes --------------------------------------------------------
        modelBuilder.Entity<Recipe>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.Title);
            e.HasMany(x => x.Ingredients).WithOne(i => i.Recipe).HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Steps).WithOne(s => s.Recipe).HasForeignKey(s => s.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tags).WithOne(t => t.Recipe).HasForeignKey(t => t.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RecipeIngredient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Quantity).HasPrecision(12, 4);
            e.Property(x => x.OriginalQuantity).HasPrecision(12, 4);
            e.HasIndex(x => x.RecipeId);
        });
        modelBuilder.Entity<RecipeStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.RecipeId);
        });
        modelBuilder.Entity<RecipeTag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.RecipeId);
            e.HasIndex(x => x.Tag);
        });

        // ----- Planning -------------------------------------------------------
        modelBuilder.Entity<WeekPlan>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.WeekStart).IsUnique();
            e.HasMany(x => x.Slots).WithOne(s => s.WeekPlan).HasForeignKey(s => s.WeekPlanId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MealSlot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.WeekPlanId);
            e.HasIndex(x => x.RecipeId);
            e.HasMany(x => x.Attendees).WithOne(a => a.MealSlot).HasForeignKey(a => a.MealSlotId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MealSlotAttendee>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.MealSlotId);
            e.HasIndex(x => x.FamilyMemberId);
            e.HasIndex(x => x.GuestId);
        });

        // ----- History --------------------------------------------------------
        modelBuilder.Entity<DishHistoryEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.RecipeId);
        });
        modelBuilder.Entity<DishFavorite>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.FamilyMemberId);
            e.HasIndex(x => x.RecipeId);
        });

        // ----- Shopping -------------------------------------------------------
        modelBuilder.Entity<ShoppingList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasMany(x => x.Items).WithOne(i => i.ShoppingList).HasForeignKey(i => i.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ShoppingListItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Quantity).HasPrecision(12, 4);
            e.HasIndex(x => x.ShoppingListId);
        });

        // ----- Settings -------------------------------------------------------
        modelBuilder.Entity<UserSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // ----- Agents: learned recipe sources --------------------------------
        modelBuilder.Entity<LearnedRecipeSource>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.LearnedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.Host).IsUnique();
            // BlueprintJson is plain text on the model; in PG it's stored as jsonb.
            e.Property(x => x.BlueprintJson).HasColumnType("jsonb");
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
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case FamilyMember f: f.UpdatedAt = now; break;
                case Guest g: g.UpdatedAt = now; break;
                case Recipe r: r.UpdatedAt = now; break;
                case WeekPlan w: w.UpdatedAt = now; break;
                case ShoppingList s: s.UpdatedAt = now; break;
                case UserSetting u: u.UpdatedAt = now; break;
            }
        }
    }
}
