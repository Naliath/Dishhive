using Dishhive.Api.Data;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Persistence behavior of the capability service: the verdict is stored per AI
/// configuration fingerprint, reused across restarts while the config is unchanged,
/// and refreshed when the config changes or a re-test is triggered.
/// </summary>
public class AiModelCapabilityServiceTests
{
    /// <summary>Tester stub: returns a canned result and counts actual runs</summary>
    private sealed class StubTester : AiModelTester
    {
        private readonly AiModelTestResult _result;
        public int Runs { get; private set; }

        public StubTester(AiOptions options, AiModelTestResult result)
            : base(Substitute.For<IChatClient>(), options,
                Substitute.For<IHttpClientFactory>(), NullLogger<AiModelTester>.Instance)
            => _result = result;

        public override Task<AiModelTestResult> RunAsync(
            string effectiveSystemPrompt, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Tester stub whose run stays in flight until the test completes it</summary>
    private sealed class BlockingTester : AiModelTester
    {
        private readonly TaskCompletionSource<AiModelTestResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Runs { get; private set; }

        public BlockingTester(AiOptions options)
            : base(Substitute.For<IChatClient>(), options,
                Substitute.For<IHttpClientFactory>(), NullLogger<AiModelTester>.Instance)
        {
        }

        public override Task<AiModelTestResult> RunAsync(
            string effectiveSystemPrompt, CancellationToken cancellationToken = default)
        {
            Runs++;
            return _completion.Task;
        }

        public void Complete(AiModelTestResult result) => _completion.SetResult(result);
    }

    private static AiOptions Options(string model = "model-a") =>
        new() { Provider = "lmstudio", Model = model };

    private static AiModelTestResult ResultFor(AiOptions options, string checkDetail) => new()
    {
        TestedAt = DateTimeOffset.UtcNow,
        Provider = options.Provider,
        Model = options.Model,
        EndpointReachable = true,
        ModelListed = true,
        ResponseMode = AiResponseMode.PromptedJson,
        EvaluationPassed = true,
        Checks = [new AiModelTestCheck("Endpoint", true, checkDetail)],
        TokensPerSecond = 12.5,
        ElapsedMs = 1234
    };

    /// <summary>Each service instance gets its own scope factory; sharing the database
    /// name simulates a restart against the same persisted store</summary>
    private static IServiceScopeFactory ScopeFactory(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DishhiveDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<AiPromptService>();
        services.AddScoped<IAiPromptProvider>(sp => sp.GetRequiredService<AiPromptService>());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static (AiModelCapabilityService Service, StubTester Tester) Create(
        string dbName, AiOptions options, string checkDetail = "probe ok")
    {
        var tester = new StubTester(options, ResultFor(options, checkDetail));
        var service = new AiModelCapabilityService(
            tester, options, ScopeFactory(dbName), NullLogger<AiModelCapabilityService>.Instance);
        return (service, tester);
    }

    [Fact]
    public async Task EnsureTested_FirstRun_TestsOnceAndPersists()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var options = Options();
        var (service, tester) = Create(dbName, options);

        var result = await service.EnsureTestedAsync();
        await service.EnsureTestedAsync(); // second await joins the same shared run

        tester.Runs.Should().Be(1);
        result.Verdict.Should().Be("passed");

        using var scope = ScopeFactory(dbName).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var record = db.AiModelTestRecords.Single();
        // Keyed by the static config fingerprint plus the effective-prompt hash
        record.ConfigKey.Should().StartWith(options.CapabilityFingerprint).And.Contain("|prompt:");
        record.ResponseMode.Should().Be("PromptedJson");
    }

    [Fact]
    public async Task EnsureTested_RestartWithSameConfig_ReusesPersistedResult()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var (first, firstTester) = Create(dbName, Options(), checkDetail: "original run");
        await first.EnsureTestedAsync();
        firstTester.Runs.Should().Be(1);

        // "Restart": a fresh service against the same store, same config
        var (second, secondTester) = Create(dbName, Options());
        var reloaded = await second.EnsureTestedAsync();

        secondTester.Runs.Should().Be(0);
        reloaded.Verdict.Should().Be("passed");
        reloaded.ResponseMode.Should().Be(AiResponseMode.PromptedJson);
        // Check details survive the JSON round-trip through the store
        reloaded.Checks.Should().ContainSingle(c => c.Detail == "original run");
    }

    [Fact]
    public async Task EnsureTested_RestartWithChangedConfig_RunsFreshTest()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var (first, _) = Create(dbName, Options(model: "model-a"));
        await first.EnsureTestedAsync();

        // Same store, but the configured model changed → fingerprint differs
        var (second, secondTester) = Create(dbName, Options(model: "model-b"));
        var result = await second.EnsureTestedAsync();

        secondTester.Runs.Should().Be(1);
        result.Model.Should().Be("model-b");
    }

    [Fact]
    public async Task EnsureTested_PromptEdited_RunsFreshTest()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var (first, _) = Create(dbName, Options());
        await first.EnsureTestedAsync();

        // The user customizes the editable prompt: the effective-prompt hash in the
        // fingerprint changes, so the persisted verdict no longer applies
        using (var scope = ScopeFactory(dbName).CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AiPromptService>()
                .SetOverrideAsync("You are a strict vegan chef.");
        }

        var (second, secondTester) = Create(dbName, Options());
        await second.EnsureTestedAsync();

        secondTester.Runs.Should().Be(1);
    }

    [Fact]
    public async Task Retest_WhileATestIsRunning_JoinsItInsteadOfStartingASecond()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var options = Options();
        var tester = new BlockingTester(options);
        var service = new AiModelCapabilityService(
            tester, options, ScopeFactory(dbName), NullLogger<AiModelCapabilityService>.Instance);

        var first = service.RetestAsync();
        service.State.Should().Be(AiModelTestState.Running);

        // Both a second re-test request and a suggestion call arriving mid-run
        // must attach to the in-flight test, never start another
        var second = service.RetestAsync();
        var waiter = service.EnsureTestedAsync();
        second.Should().BeSameAs(first);

        tester.Complete(ResultFor(options, "single run"));
        var results = await Task.WhenAll(first, second, waiter);

        tester.Runs.Should().Be(1);
        service.State.Should().Be(AiModelTestState.Completed);
        results.Should().OnlyContain(r => r.Verdict == "passed");
    }

    [Fact]
    public async Task Retest_RunsDespitePersistedResult_AndUpdatesTheStore()
    {
        var dbName = $"cap-{Guid.NewGuid()}";
        var (first, _) = Create(dbName, Options(), checkDetail: "first run");
        await first.EnsureTestedAsync();

        var (second, secondTester) = Create(dbName, Options(), checkDetail: "re-test run");
        await second.RetestAsync();
        secondTester.Runs.Should().Be(1);

        // A later restart sees the updated result, still as a single row
        var (third, thirdTester) = Create(dbName, Options());
        var reloaded = await third.EnsureTestedAsync();
        thirdTester.Runs.Should().Be(0);
        reloaded.Checks.Should().ContainSingle(c => c.Detail == "re-test run");
    }
}
