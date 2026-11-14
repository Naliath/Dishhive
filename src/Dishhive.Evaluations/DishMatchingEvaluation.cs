using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Evaluations;

public sealed record DishMatchingCase(string Id, string FoodRequest, string Title, string[] Ingredients,
    string[] Instructions, bool ExpectedMatch);
public sealed record DishMatchingCorpus(int SchemaVersion, string Scope, Dictionary<string, string[]> RecipeFamilies,
    List<DishMatchingCase> Cases)
{
    public static DishMatchingCorpus Load(string path)
    {
        var corpus = JsonSerializer.Deserialize<DishMatchingCorpus>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing dish-matching corpus");
        if (corpus.SchemaVersion != 1 || corpus.Cases.Count == 0 || corpus.Cases.Select(item => item.Id).Distinct().Count() != corpus.Cases.Count
            || corpus.Cases.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.FoodRequest)
                || string.IsNullOrWhiteSpace(item.Title) || item.Ingredients.Length == 0 || item.Instructions.Length == 0)
            || !corpus.Cases.Any(item => item.ExpectedMatch) || !corpus.Cases.Any(item => !item.ExpectedMatch)
            || corpus.RecipeFamilies.Any(family => family.Value.Length == 0 || family.Value.Any(url => !Uri.IsWellFormedUriString(url, UriKind.Absolute))))
            throw new InvalidDataException("Invalid independent dish-matching labels");
        return corpus;
    }
}

internal static class DishMatchingEvaluation
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string Argument(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0 ? args[index + 1] : fallback;
        var path = Argument("--corpus", Path.Combine(AppContext.BaseDirectory, "dish-matching-scenarios.json"));
        var corpus = DishMatchingCorpus.Load(path);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        if (args.Contains("--validate-only"))
        { Console.WriteLine($"Validated {corpus.Cases.Count} independent food-identity cases; corpus={hash}"); return 0; }
        var iterations = int.Parse(Argument("--iterations", "3"));
        if (iterations is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(iterations));
        var ids = Argument("--case", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var cases = corpus.Cases.Where(item => ids.Count == 0 || ids.Contains(item.Id)).ToList();
        if (cases.Count == 0 || ids.Count > 0 && cases.Count != ids.Count) throw new InvalidDataException("Unknown food-identity case");
        var options = new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Ai").Get<AiOptions>() ?? new();
        options.Validate();
        if (!options.IsConfigured) throw new InvalidOperationException("AI configuration is required");
        using var client = ChatClientFactory.Create(options);
        var metrics = new Metrics();
        var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);
        var matcher = new RecipeDishMatcher(gate, NullLogger<RecipeDishMatcher>.Instance);
        var results = new List<object>();
        var passed = 0;
        var durations = new List<long>();
        var started = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        foreach (var item in cases)
        for (var iteration = 1; iteration <= iterations; iteration++)
        {
            var operation = $"dish-eval-{Guid.NewGuid():N}";
            var timer = Stopwatch.StartNew();
            var matches = await matcher.MatchAsync(item.FoodRequest,
                [new("candidate", item.Title, item.Ingredients, item.Instructions)], operation, cancellation.Token);
            var correct = matches is not null && matches.Contains("candidate") == item.ExpectedMatch;
            if (correct) passed++;
            durations.Add(timer.ElapsedMilliseconds);
            var calls = metrics.Runs.Values.Where(run => run.OperationId == operation).ToList();
            results.Add(new { item.Id, Iteration = iteration, Passed = correct, item.ExpectedMatch,
                HasResult = matches is not null, ActualMatch = matches?.Contains("candidate"), DurationMs = timer.ElapsedMilliseconds,
                Calls = calls.Select(run => new { run.Task, run.ContractVersion, run.Outcome, run.OperationAttempt,
                    run.DurationMs, run.TotalTokens, run.PromptHash, run.InputFingerprint, run.ReasoningDisabled, run.ResponseMode, run.SchemaApplied }) });
            Console.WriteLine($"{item.Id} {iteration}/{iterations}: {(correct ? "PASS" : "FAIL")} ({timer.ElapsedMilliseconds}ms)");
        }
        var output = Path.GetFullPath(Argument("--output", "artifacts/dish-matching-evals/latest.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { CorpusHash = hash, corpus.Scope,
            Provider = options.Provider, Model = options.Model, ConfigFingerprint = options.ExecutionFingerprint,
            StartedAt = started, CompletedAt = DateTimeOffset.UtcNow, CaseCount = cases.Count, Iterations = iterations,
            Passed = passed, Failed = results.Count - passed, AverageDurationMs = durations.Average(),
            P95DurationMs = durations.Order().ElementAt((int)Math.Ceiling(durations.Count * .95) - 1), Results = results },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Console.WriteLine($"{passed} passed; {results.Count - passed} failed. Report: {output}");
        return passed == results.Count ? 0 : 1;
    }
    private sealed class Metrics : IAiTaskMetricsStore
    {
        public ConcurrentDictionary<Guid, AiTaskRun> Runs { get; } = new();
        public Task SaveAsync(AiTaskRun run) { Runs[run.Id] = run; return Task.CompletedTask; }
        public Task UpdateOutcomeAsync(Guid id, string outcome, string? error = null)
        { Runs[id].Outcome = outcome; return Task.CompletedTask; }
    }
}
