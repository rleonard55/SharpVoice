namespace SharpVoice.Diagnostics;

internal static class DiagnosticShutdown
{
    // A worker fault must never bypass release of the microphone and detector.
    internal static async Task<Exception?> RunAsync(Task work, Func<Task> release)
    {
        Exception? failure = null;
        try { await work; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failure = ex; }
        finally { await release(); }
        return failure;
    }
}
