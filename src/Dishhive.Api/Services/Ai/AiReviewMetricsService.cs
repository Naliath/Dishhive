using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Ai;

public sealed class AiReviewMetricsService(DishhiveDbContext context)
{
    public async Task<AiReviewMetricsDto> LoadAsync(int count, DateTime? from, string? task, string? model,
        string? fingerprint, string? outcome, string? operationId, CancellationToken token)
    {
        var query = context.AiReviewEvents.AsNoTracking();
        if (from.HasValue) query = query.Where(review => review.OccurredAt >= from);
        if (!string.IsNullOrWhiteSpace(task)) query = query.Where(review => review.Task == task);
        if (!string.IsNullOrWhiteSpace(operationId)) query = query.Where(review => review.OperationId == operationId);
        if (!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(fingerprint) || !string.IsNullOrWhiteSpace(outcome))
        {
            var calls = context.AiTaskRuns.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(model)) calls = calls.Where(call => call.Model == model);
            if (!string.IsNullOrWhiteSpace(fingerprint)) calls = calls.Where(call => call.ConfigFingerprint == fingerprint);
            if (!string.IsNullOrWhiteSpace(outcome)) calls = calls.Where(call => call.Outcome == outcome);
            if (!string.IsNullOrWhiteSpace(task)) calls = calls.Where(call => call.Task == task);
            var operations = calls.Select(call => call.OperationId).Distinct();
            query = query.Where(review => review.OperationId != null && operations.Contains(review.OperationId));
        }
        var reviews = await query.OrderByDescending(review => review.OccurredAt).ThenByDescending(review => review.Id).ToListAsync(token);
        var suggestions = reviews.Where(review => review.Kind == "suggestionReviewed").ToList();
        return new(reviews.Count,
            reviews.Count(review => review.Kind is "factsConfirmed" or "factsCorrected" or "factsSetAfterSourceEdit"),
            reviews.Count(review => review.Kind == "factsCorrected"),
            reviews.Count(review => review.Kind == "textEditedAfterTranslation"),
            suggestions.DistinctBy(review => (review.BatchId, review.SuggestionId)).Count(),
            suggestions.Where(review => review.ChangedFieldsJson != "[]").DistinctBy(review => (review.BatchId, review.SuggestionId)).Count(),
            suggestions.Where(review => review.InputChangesJson != "[]").DistinctBy(review => (review.BatchId, review.SuggestionId)).Count(),
            reviews.Take(count).Select(review => new AiReviewEventDto(review.Id, review.OccurredAt, review.Task, review.Kind,
                review.OperationId, review.RecipeId, review.BatchId, review.SuggestionId, review.Outcome,
                review.SourceRevision, review.ObservedRevision, review.SourceConfirmationVersion, review.ObservedConfirmationVersion,
                JsonSerializer.Deserialize<List<string>>(review.ChangedFieldsJson) ?? [],
                JsonSerializer.Deserialize<List<string>>(review.InputChangesJson) ?? [])).ToList());
    }
}
