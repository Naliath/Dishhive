using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Evaluations;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--dish-matching")) return await DishMatchingEvaluation.RunAsync(args);
            if (args.Contains("--cup-translation")) return await CupTranslationEvaluation.RunAsync(args);
            string Argument(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0
                ? args[index + 1] : fallback;
            var path = Argument("--corpus", Path.Combine(AppContext.BaseDirectory, "facts-scenarios.json"));
            var corpus = FactsEvaluationCorpus.Load(path);
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            if (args.Contains("--validate-only"))
            {
                Console.WriteLine($"Validated {corpus.Cases.Count} independently labelled cases; corpus={hash}");
                return 0;
            }
            var iterations = int.Parse(Argument("--iterations", "3"));
            if (iterations is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(iterations));
            var ids = Argument("--case", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var cases = ids.Count == 0 ? corpus.Cases : corpus.Cases.Where(item => ids.Contains(item.Id)).ToList();
            if (ids.Count > 0 && cases.Count != ids.Count) throw new InvalidDataException("Unknown evaluation case ID");
            var options = new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Ai").Get<AiOptions>() ?? new();
            options.Validate();
            if (!options.IsConfigured) throw new InvalidOperationException("AI environment configuration is required");
            using var client = ChatClientFactory.Create(options);
            var metrics = new EvaluationMetrics();
            var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);
            var extractor = new LlmRecipeFactsExtractor(gate, NullLogger<LlmRecipeFactsExtractor>.Instance);
            var results = new List<object>();
            var passed = 0;
            var failed = 0;
            var started = DateTimeOffset.UtcNow;
            var durations = new List<long>();
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
            foreach (var item in cases)
            for (var iteration = 1; iteration <= iterations; iteration++)
            {
                var operation = $"facts-eval-{Guid.NewGuid():N}";
                var timer = Stopwatch.StartNew();
                var actual = await extractor.ExtractAsync(item.Title, item.Ingredients, cancellation.Token, operation);
                var assessment = item.Assess(actual);
                durations.Add(timer.ElapsedMilliseconds);
                if (assessment.Passed) passed++; else failed++;
                var calls = metrics.Runs.Values.Where(run => run.OperationId == operation).OrderBy(run => run.OperationAttempt).ToList();
                results.Add(new { item.Id, Iteration = iteration, assessment.Passed, assessment.HasResult,
                    assessment.Missing, assessment.Unexpected, Actual = actual == null ? null : IngredientClasses.ToNames(actual),
                    item.Exact, DurationMs = timer.ElapsedMilliseconds, OperationId = operation,
                    Calls = calls.Select(run => new { run.Task, run.ContractVersion, run.Outcome, run.OperationAttempt,
                        run.DurationMs, run.TotalTokens, run.PromptHash, run.InputFingerprint,
                        run.Temperature, run.MaxOutputTokens, run.ReasoningDisabled, run.ReasoningTokens,
                        run.ResponseMode, run.SchemaApplied }) });
                Console.WriteLine($"{item.Id} {iteration}/{iterations}: {(assessment.Passed ? "PASS" : "FAIL")} ({timer.ElapsedMilliseconds}ms)");
            }
            var output = Path.GetFullPath(Argument("--output", $"artifacts/facts-evals/facts-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { CorpusHash = hash, corpus.Scope,
                Provider = options.Provider, Model = options.Model, ConfigFingerprint = options.ExecutionFingerprint,
                StartedAt = started, CompletedAt = DateTimeOffset.UtcNow,
                CaseCount = cases.Count, Iterations = iterations, Passed = passed, Failed = failed,
                AverageDurationMs = durations.Average(),
                P95DurationMs = durations.Order().ElementAt((int)Math.Ceiling(durations.Count * .95) - 1), Results = results },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            Console.WriteLine($"{passed} passed; {failed} failed. Report: {output}");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            // Provider exception messages can include response content; keep console failures safe.
            Console.Error.WriteLine($"Facts evaluation failed: {error.GetType().Name}");
            return 2;
        }
    }

    private sealed class EvaluationMetrics : IAiTaskMetricsStore
    {
        public ConcurrentDictionary<Guid, AiTaskRun> Runs { get; } = new();
        public Task SaveAsync(AiTaskRun run) { Runs[run.Id] = run; return Task.CompletedTask; }
        public Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null)
        {
            if (Runs.TryGetValue(runId, out var run)) run.Outcome = outcome;
            return Task.CompletedTask;
        }
    }
}
