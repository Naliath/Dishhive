namespace Dishhive.Api.Services.Ai;

/// <summary>One deadline, correlation id and provider-call allowance inherited by nested work.</summary>
public sealed class AiOperationBudget : IDisposable
{
    private static readonly AsyncLocal<AiOperationBudget?> Ambient = new();
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationToken _caller;
    private readonly AiOperationBudget? _parent;
    private readonly AiOperationBudget? _previous;
    private readonly AttemptCounter _counter;
    private sealed class AttemptCounter(int maximum) { public readonly int Maximum = maximum; public int Count; }

    public AiOperationBudget(int timeoutSeconds, int maxAttempts, CancellationToken cancellationToken,
        string? operationId = null, bool independent = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _previous = Ambient.Value;
        _parent = independent ? null : _previous;
        _counter = _parent?._counter ?? new AttemptCounter(maxAttempts);
        _caller = cancellationToken;
        OperationId = _parent?.OperationId ?? operationId ?? Guid.NewGuid().ToString("N")[..12];
        DeadlineUtc = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        if (_parent is not null && _parent.DeadlineUtc < DeadlineUtc) DeadlineUtc = _parent.DeadlineUtc;
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _parent?.Token ?? CancellationToken.None);
        _deadline.CancelAfter(TimeSpan.FromTicks(Math.Max(1, (DeadlineUtc - DateTime.UtcNow).Ticks)));
        Ambient.Value = this;
    }

    internal static AiOperationBudget? Current => Ambient.Value;
    public string OperationId { get; }
    public DateTime DeadlineUtc { get; }
    public CancellationToken Token => _deadline.Token;
    public int Attempts => Volatile.Read(ref _counter.Count);
    public int RemainingAttempts => Math.Max(0, _counter.Maximum - Attempts);
    public bool CallerCancelled => _parent?.CallerCancelled == true
        || (_caller != _parent?.Token && _caller.IsCancellationRequested);
    public int TakeAttempt()
    {
        Token.ThrowIfCancellationRequested();
        if (DateTime.UtcNow >= DeadlineUtc) throw new OperationCanceledException(Token);
        while (true)
        {
            var count = Attempts;
            if (count >= _counter.Maximum) throw new AiOperationBudgetExceededException();
            if (Interlocked.CompareExchange(ref _counter.Count, count + 1, count) == count) return count + 1;
        }
    }
    public void Dispose()
    {
        if (ReferenceEquals(Ambient.Value, this)) Ambient.Value = _previous;
        _deadline.Dispose();
    }
}

public sealed class AiOperationBudgetExceededException()
    : InvalidOperationException("The AI operation exhausted its total provider-attempt budget");
