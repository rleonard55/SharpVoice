using SharpVoice.Core;

internal static class SelectionOwnershipChecks
{
    public static async Task Run()
    {
        var source = new Resource();
        var completed = new DeferredDisposable<Resource>(Task.FromResult<Resource?>(source));
        // A post-roll cancellation happens before the captured source is acquired by processing.
        try { using (completed) throw new OperationCanceledException(); }
        catch (OperationCanceledException) { }
        completed.Dispose();
        await completed.Disposal;
        if (source.Releases != 1) throw new Exception("Captured selection leaked or was released twice after post-roll cancellation.");
        var pending = new TaskCompletionSource<Resource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new Resource();
        var cancelled = new DeferredDisposable<Resource>(pending.Task);
        cancelled.Dispose();
        pending.SetResult(late);
        await cancelled.Disposal;
        if (late.Releases != 1) throw new Exception("Late accessibility selection escaped cancellation cleanup.");
        using var failed = new DeferredDisposable<Resource>(Task.FromException<Resource?>(new IOException("Provider failed")));
        failed.Dispose();
        await failed.Disposal;
    }
    private sealed class Resource : IDisposable
    {
        public int Releases;
        public void Dispose() => Releases++;
    }
}
