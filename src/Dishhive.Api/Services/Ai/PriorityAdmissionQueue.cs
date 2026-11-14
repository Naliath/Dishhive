namespace Dishhive.Api.Services.Ai;

/// <summary>
/// Bounded in-process admission for a single model provider. Waiting interactive
/// work is admitted before bulk enrichment without interrupting work already sent
/// to the provider.
/// </summary>
internal sealed class PriorityAdmissionQueue
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<IDisposable>> _interactive = new();
    private readonly Queue<TaskCompletionSource<IDisposable>> _background = new();
    private int _available;
    private readonly int _maxQueued;

    public PriorityAdmissionQueue(int capacity, int maxQueued = 32)
    {
        _available = Math.Max(1, capacity);
        _maxQueued = maxQueued;
    }

    public Task<IDisposable> EnterAsync(AiRequestPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_available > 0)
            {
                _available--;
                return Task.FromResult<IDisposable>(new Lease(this));
            }

            RemoveCancelled(_interactive);
            RemoveCancelled(_background);
            if (_interactive.Count + _background.Count >= _maxQueued)
                throw new InvalidOperationException("The AI waiting queue is full; retry later");
            var waiter = new TaskCompletionSource<IDisposable>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            (priority == AiRequestPriority.Interactive ? _interactive : _background).Enqueue(waiter);
            return AwaitAsync(waiter, cancellationToken);
        }
    }

    private static void RemoveCancelled(Queue<TaskCompletionSource<IDisposable>> queue)
    {
        var count = queue.Count;
        for (var index = 0; index < count; index++)
        {
            var waiter = queue.Dequeue();
            if (!waiter.Task.IsCompleted) queue.Enqueue(waiter);
        }
    }

    private static async Task<IDisposable> AwaitAsync(
        TaskCompletionSource<IDisposable> waiter,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            () => waiter.TrySetCanceled(cancellationToken));
        return await waiter.Task.ConfigureAwait(false);
    }

    private void Release()
    {
        lock (_sync)
        {
            while (TryTakeNext(out var waiter))
            {
                if (waiter.TrySetResult(new Lease(this))) return;
            }
            _available++;
        }
    }

    private bool TryTakeNext(out TaskCompletionSource<IDisposable> waiter)
    {
        if (_interactive.TryDequeue(out waiter!)) return true;
        return _background.TryDequeue(out waiter!);
    }

    private sealed class Lease(PriorityAdmissionQueue owner) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release();
        }
    }
}
