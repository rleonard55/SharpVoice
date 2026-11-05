namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private int memoryObservedGeneration, memoryReclaimedGeneration;
    private long memoryIdleSince;

    // Run on the message thread. A silent/canceled capture can finish before its
    // native model preload or draft has drained, so trim only after full idle.
    private void ReclaimIdleMemory()
    {
        var now = Environment.TickCount64;
        var idle = UpdateIdle;
        lock (liveGate) idle &= liveTranscription is null && liveStopping.IsCompleted;
        if (!idle || memoryObservedGeneration != generation)
        {
            memoryObservedGeneration = generation;
            memoryIdleSince = now;
            return;
        }
        if (generation == 0 || memoryReclaimedGeneration == generation || now - memoryIdleSince < 2000) return;
        environment.TrimMemory();
        memoryReclaimedGeneration = generation;
    }
}
