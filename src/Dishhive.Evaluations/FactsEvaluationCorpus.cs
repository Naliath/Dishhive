using System.Text.Json;
using Dishhive.Api.Models;

namespace Dishhive.Evaluations;

public sealed record FactsEvaluationCase
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required List<string> Ingredients { get; init; }
    public required List<string> RequiredClasses { get; init; }
    public List<string> ForbiddenClasses { get; init; } = [];
    public bool Exact { get; init; } = true;
    public required string Basis { get; init; }
    public string? SourceUrl { get; init; }

    public FactsAssessment Assess(IReadOnlyList<IngredientClass>? actual)
    {
        if (actual == null) return new(false, false, RequiredClasses, []);
        var values = actual.ToHashSet();
        var required = RequiredClasses.Select(Parse).ToHashSet();
        var forbidden = ForbiddenClasses.Select(Parse).ToHashSet();
        var missing = required.Except(values).Order().Select(EnumNames.ToName).ToList();
        var unexpected = values.Where(value => Exact ? !required.Contains(value) : forbidden.Contains(value))
            .Order().Select(EnumNames.ToName).ToList();
        return new(missing.Count == 0 && unexpected.Count == 0, true, missing, unexpected);
    }

    internal static IngredientClass Parse(string name) => IngredientClasses.TryParse(name, out var value)
        ? value : throw new InvalidDataException($"Unknown ingredient class: {name}");
}

public sealed record FactsAssessment(bool Passed, bool HasResult, IReadOnlyList<string> Missing, IReadOnlyList<string> Unexpected);

public sealed record FactsEvaluationCorpus
{
    public int SchemaVersion { get; init; }
    public required string Scope { get; init; }
    public required List<string> References { get; init; }
    public required List<FactsEvaluationCase> Cases { get; init; }
    public static FactsEvaluationCorpus Load(string path)
    {
        var corpus = JsonSerializer.Deserialize<FactsEvaluationCorpus>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Missing corpus");
        if (corpus.SchemaVersion != 1 || corpus.Cases.Count == 0 || corpus.Cases.Select(item => item.Id).Distinct().Count() != corpus.Cases.Count)
            throw new InvalidDataException("Invalid facts corpus version or duplicate case IDs");
        foreach (var item in corpus.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || item.Ingredients.Count == 0 || string.IsNullOrWhiteSpace(item.Basis))
                throw new InvalidDataException("Cases require an ID, ingredients and an independent expectation basis");
            var required = item.RequiredClasses.Select(FactsEvaluationCase.Parse).ToHashSet();
            if (item.ForbiddenClasses.Select(FactsEvaluationCase.Parse).Any(required.Contains))
                throw new InvalidDataException("A class cannot be both required and forbidden");
        }
        return corpus;
    }
}
