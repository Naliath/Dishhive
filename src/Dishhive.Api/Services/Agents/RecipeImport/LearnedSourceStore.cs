using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models.Agents;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Agents.RecipeImport;

/// <summary>
/// Read/write access to <see cref="LearnedRecipeSource"/> rows.
/// Bookkeeping (incrementing <c>UseCount</c>, updating <c>LastUsedAt</c>) lives here
/// rather than scattered across providers.
/// </summary>
public interface ILearnedSourceStore
{
    Task<LearnedRecipeSource?> FindByHostAsync(string host, CancellationToken ct = default);
    Task<IReadOnlyList<LearnedRecipeSource>> ListAsync(CancellationToken ct = default);
    Task<LearnedRecipeSource> UpsertAsync(string host, RecipeImportBlueprint blueprint, string sourceUrl, CancellationToken ct = default);
    Task<bool> DeleteByHostAsync(string host, CancellationToken ct = default);
    Task RecordUseAsync(Guid id, CancellationToken ct = default);
}

public sealed class LearnedSourceStore : ILearnedSourceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DishhiveDbContext _db;

    public LearnedSourceStore(DishhiveDbContext db) => _db = db;

    public Task<LearnedRecipeSource?> FindByHostAsync(string host, CancellationToken ct = default) =>
        _db.LearnedRecipeSources.FirstOrDefaultAsync(x => x.Host == host.ToLower(), ct);

    public async Task<IReadOnlyList<LearnedRecipeSource>> ListAsync(CancellationToken ct = default) =>
        await _db.LearnedRecipeSources
            .OrderByDescending(x => x.LastUsedAt ?? x.LearnedAt)
            .ToListAsync(ct);

    public async Task<LearnedRecipeSource> UpsertAsync(string host, RecipeImportBlueprint blueprint, string sourceUrl, CancellationToken ct = default)
    {
        var key = host.ToLowerInvariant();
        var json = JsonSerializer.Serialize(blueprint, JsonOptions);
        var existing = await _db.LearnedRecipeSources.FirstOrDefaultAsync(x => x.Host == key, ct);
        if (existing is null)
        {
            existing = new LearnedRecipeSource
            {
                Host = key,
                ProviderKey = $"learned:{key}",
                Strategy = blueprint.Strategy.ToString(),
                BlueprintJson = json,
                SourceUrl = sourceUrl,
            };
            _db.LearnedRecipeSources.Add(existing);
        }
        else
        {
            existing.Strategy = blueprint.Strategy.ToString();
            existing.BlueprintJson = json;
            existing.SourceUrl = sourceUrl;
        }
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteByHostAsync(string host, CancellationToken ct = default)
    {
        var key = host.ToLowerInvariant();
        var existing = await _db.LearnedRecipeSources.FirstOrDefaultAsync(x => x.Host == key, ct);
        if (existing is null) return false;
        _db.LearnedRecipeSources.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RecordUseAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.LearnedRecipeSources.FindAsync(new object?[] { id }, ct);
        if (row is null) return;
        row.LastUsedAt = DateTime.UtcNow;
        row.UseCount++;
        await _db.SaveChangesAsync(ct);
    }

    public static RecipeImportBlueprint Deserialize(string json) =>
        JsonSerializer.Deserialize<RecipeImportBlueprint>(json, JsonOptions)
            ?? new RecipeImportBlueprint { Strategy = LearnedRecipeSourceStrategy.JsonLd };
}
