namespace SharpVoice.UI;

internal sealed class CueResourcesRetainedException(Exception inner) : Exception("The audio driver retained cue resources.", inner);

internal static class CueOutputRecovery
{
    internal static T? TryOpen<T>(Func<T> create, ref bool blocked) where T : class
    {
        if (blocked) return null;
        try { return create(); }
        catch (CueResourcesRetainedException) { blocked = true; return null; }
        catch { return null; } // Ordinary output loss can recover on a subsequent cue.
    }
}
