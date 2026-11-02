namespace SharpVoice.Core;

/// <summary>Owns an asynchronous resource even if the caller exits before awaiting its value.</summary>
public sealed class DeferredDisposable<T>(Task<T?> value) : IDisposable where T : class, IDisposable
{
    private int disposed;
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<T?> Value { get; } = value;
    public Task Disposal => released.Task;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) _ = ReleaseAsync();
    }
    private async Task ReleaseAsync()
    {
        try { (await Value.ConfigureAwait(false))?.Dispose(); }
        catch { /* Observe failed/cancelled acquisition; ownership cleanup must still finish. */ }
        finally { released.TrySetResult(); }
    }
}
