namespace SharpVoice.UI;

internal static class WaveOutCleanup
{
    // Returns false when the driver may still own resources; callers must retain the callback handle.
    internal static bool TryRelease(IEnumerable<nint> buffers, Action reset, Func<nint, bool> unprepare, Action<nint> free, Func<bool> close, Action releaseCallback)
    {
        reset();
        var returned = true;
        foreach (var buffer in buffers)
        {
            if (unprepare(buffer)) free(buffer);
            else returned = false;
        }
        if (!returned || !close()) return false;
        releaseCallback();
        return true;
    }
}
