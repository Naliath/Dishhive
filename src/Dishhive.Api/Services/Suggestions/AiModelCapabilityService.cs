using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Lifecycle of the model capability test.</summary>
public enum AiModelTestState
{
    NotConfigured,
    NotRun,
    Running,
    Completed
}

/// <summary>
/// Holds the capability-test verdict for the configured model and makes AI work wait
/// for it: a model must have a test result before the first real suggestion call
/// (see docs/features/ai-week-planning.md). The verdict is persisted (AiModelTestRecord,
/// keyed by AiOptions.CapabilityFingerprint + a hash of the effective system prompt),
/// so a restart with unchanged AI settings reuses it instead of re-testing on every
/// boot; a fresh test runs only when the fingerprint is new — AI settings changed, the
/// user edited the prompt, or an app update changed the shipped prompt — or when the
/// user re-triggers it from the settings page. Note the
/// trade-off: a server-side change behind the same settings (a different model loaded
/// into LM Studio under the same id, a changed context length) is NOT auto-detected —
/// that's what the settings-page re-test button is for.
/// </summary>
public interface IAiModelCapabilityService
{
    AiModelTestState State { get; }

    /// <summary>Last completed result, if any</summary>
    AiModelTestResult? Result { get; }

    /// <summary>
    /// The result for the configured model: the persisted verdict when the AI settings
    /// are unchanged, otherwise a fresh test. Callers doing AI work await this — the
    /// test is the gate, everything else in the application stays usable while it runs.
    /// </summary>
    Task<AiModelTestResult> EnsureTestedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a fresh test run regardless of any persisted result (settings-page
    /// re-trigger after the user tweaked the model or its server-side setup).
    /// Joins the in-flight run when one is already going.
    /// </summary>
    Task<AiModelTestResult> RetestAsync();
}

public class AiModelCapabilityService : IAiModelCapabilityService
{
    private readonly AiModelTester _tester;
    private readonly AiOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiModelCapabilityService> _logger;
    private readonly object _gate = new();
    private Task<AiModelTestResult>? _run;

    public AiModelCapabilityService(
        AiModelTester tester, AiOptions options,
        IServiceScopeFactory scopeFactory, ILogger<AiModelCapabilityService> logger)
    {
        _tester = tester;
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public AiModelTestState State
    {
        get
        {
            var run = _run;
            return run is null ? AiModelTestState.NotRun
                : run.IsCompleted ? AiModelTestState.Completed
                : AiModelTestState.Running;
        }
    }

    public AiModelTestResult? Result =>
        _run is { IsCompletedSuccessfully: true } run ? run.Result : null;

    public Task<AiModelTestResult> EnsureTestedAsync(CancellationToken cancellationToken = default)
    {
        Task<AiModelTestResult> run;
        lock (_gate)
        {
            run = _run ??= LoadOrRunDetachedAsync();
        }
        // The run itself is shared and never cancelled; each caller only abandons its wait
        return run.WaitAsync(cancellationToken);
    }

    public Task<AiModelTestResult> RetestAsync()
    {
        lock (_gate)
        {
            if (_run is { IsCompleted: false } inFlight)
            {
                return inFlight;
            }
            _run = RetestDetachedAsync();
            return _run;
        }
    }

    /// <summary>Reuses the persisted verdict when the AI settings + effective prompt
    /// fingerprint matches; otherwise runs (and persists) a fresh test.</summary>
    private async Task<AiModelTestResult> LoadOrRunDetachedAsync()
    {
        // Yield so the lock in the caller is released before any real work starts
        await Task.Yield();
        try
        {
            var (_, key) = await ResolvePromptAndKeyAsync();
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var record = await db.AiModelTestRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ConfigKey == key);
            if (record is not null && ToResult(record) is { } stored)
            {
                _logger.LogInformation(
                    "Reusing persisted AI model test for {Provider}/{Model}: verdict={Verdict} (tested {TestedAt:u}). Re-test from the settings page if the served model changed.",
                    stored.Provider, stored.Model, stored.Verdict, stored.TestedAt);
                return stored;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the persisted AI model test; running a fresh one");
        }

        return await RunAndStoreLatestAsync();
    }

    private async Task<AiModelTestResult> RetestDetachedAsync()
    {
        await Task.Yield();
        return await RunAndStoreLatestAsync();
    }

    /// <summary>
    /// Runs the test against the current effective prompt and persists the verdict
    /// under the prompt-aware key. When the prompt was edited WHILE the test ran (the
    /// settings page allows it — a local-model test takes minutes), the just-produced
    /// verdict describes a prompt that no longer runs: loop and test again against the
    /// new one, so the shared task always completes on the latest state.
    /// </summary>
    private async Task<AiModelTestResult> RunAndStoreLatestAsync()
    {
        while (true)
        {
            var (prompt, key) = await ResolvePromptAndKeyAsync();
            // RunAsync never throws (it converts failures into a failed result), so the
            // shared task always completes successfully and Result above stays simple
            var result = await _tester.RunAsync(prompt, CancellationToken.None);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
                var record = await db.AiModelTestRecords.FirstOrDefaultAsync(r => r.ConfigKey == key);
                if (record is null)
                {
                    record = new AiModelTestRecord { ConfigKey = key };
                    db.AiModelTestRecords.Add(record);
                }
                Apply(result, record);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AI model test finished (verdict={Verdict}) but could not be persisted; it will re-run at the next startup",
                    result.Verdict);
            }

            var (_, latestKey) = await ResolvePromptAndKeyAsync();
            if (latestKey == key)
            {
                return result;
            }
            _logger.LogInformation("The AI prompt changed while its test was running; testing again against the new prompt");
        }
    }

    /// <summary>
    /// The current effective system prompt and the persistence key for its verdict:
    /// the static config fingerprint plus a hash of the full prompt text, so both a
    /// user prompt edit AND a shipped-prompt change in an app update invalidate the
    /// stored verdict. Falls back to the default prompt when the store is unreadable.
    /// </summary>
    private async Task<(string Prompt, string Key)> ResolvePromptAndKeyAsync()
    {
        string prompt;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prompts = scope.ServiceProvider.GetRequiredService<IAiPromptProvider>();
            prompt = await prompts.GetEffectiveSystemPromptAsync();
        }
        catch
        {
            prompt = LlmMealSuggestionService.ComposeSystemPrompt(null);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))[..16];
        return (prompt, $"{_options.CapabilityFingerprint}|prompt:{hash}");
    }

    private static void Apply(AiModelTestResult result, AiModelTestRecord record)
    {
        record.Provider = result.Provider;
        record.Model = result.Model;
        record.TestedAt = result.TestedAt;
        record.EndpointReachable = result.EndpointReachable;
        record.ModelListed = result.ModelListed;
        record.ResponseMode = EnumNames.ToName(result.ResponseMode);
        record.EvaluationPassed = result.EvaluationPassed;
        record.ChecksJson = JsonSerializer.Serialize(result.Checks);
        record.TokensPerSecond = result.TokensPerSecond;
        record.ElapsedMs = result.ElapsedMs;
    }

    /// <summary>Maps a stored record back to a result; null when it can't be read
    /// (unknown enum value, corrupt checks JSON) — the caller then re-tests.</summary>
    private AiModelTestResult? ToResult(AiModelTestRecord record)
    {
        if (!EnumNames.TryParse<AiResponseMode>(record.ResponseMode, out var mode))
        {
            return null;
        }

        List<AiModelTestCheck>? checks;
        try
        {
            checks = JsonSerializer.Deserialize<List<AiModelTestCheck>>(record.ChecksJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Persisted AI model test has unreadable check details; re-testing");
            return null;
        }

        return new AiModelTestResult
        {
            TestedAt = record.TestedAt,
            Provider = record.Provider,
            Model = record.Model,
            EndpointReachable = record.EndpointReachable,
            ModelListed = record.ModelListed,
            ResponseMode = mode,
            EvaluationPassed = record.EvaluationPassed,
            Checks = checks ?? [],
            TokensPerSecond = record.TokensPerSecond,
            ElapsedMs = record.ElapsedMs
        };
    }
}

/// <summary>Registered when AI is unconfigured (or in Testing): reports NotConfigured and
/// is never asked to actually test — the NoOp suggestion service short-circuits first.</summary>
public class NoOpAiModelCapabilityService : IAiModelCapabilityService
{
    public AiModelTestState State => AiModelTestState.NotConfigured;

    public AiModelTestResult? Result => null;

    public Task<AiModelTestResult> EnsureTestedAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("AI is not configured; there is no model to test.");

    public Task<AiModelTestResult> RetestAsync() =>
        throw new InvalidOperationException("AI is not configured; there is no model to test.");
}

/// <summary>
/// Resolves the capability verdict at startup so it is usually ready before the first
/// "Suggest week" click: instant when a persisted result matches the current AI
/// settings, a background test run otherwise. The application does not wait for it;
/// only AI work does (LlmMealSuggestionService awaits EnsureTestedAsync).
/// </summary>
public class AiModelStartupTest : BackgroundService
{
    private readonly IAiModelCapabilityService _capability;
    private readonly ILogger<AiModelStartupTest> _logger;

    public AiModelStartupTest(IAiModelCapabilityService capability, ILogger<AiModelStartupTest> logger)
    {
        _capability = capability;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _capability.EnsureTestedAsync(stoppingToken);
            _logger.LogInformation(
                "Startup AI model test: verdict={Verdict}, mode={Mode} ({Provider}/{Model})",
                result.Verdict, result.ResponseMode, result.Provider, result.Model);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down before the test finished; nothing to do
        }
    }
}
