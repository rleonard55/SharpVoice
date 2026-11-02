namespace SharpVoice.Speech;

public sealed record WakeDetectorDiagnostics(long Enqueued, long Processed, long Dropped, long Resets,
    int QueuedSamples, double LastWorkMs, double LongestWorkMs, long LastProcessedAgeMs, bool WorkerCompleted,
    long Checks = 0, double LastInferenceMs = 0, bool Recognizing = false);

public sealed partial class WakeKeywordDetector
{
    private readonly bool diagnostics;
    private long diagnosticEnqueued, diagnosticProcessed, diagnosticDropped, diagnosticResets, diagnosticLastProcessed;
    private double diagnosticLastMs, diagnosticLongestMs;
    public WakeDetectorDiagnostics? Diagnostics
    {
        get
        {
            if (!diagnostics) return null;
            lock (gate) return new(diagnosticEnqueued, diagnosticProcessed, diagnosticDropped, diagnosticResets,
                queued, diagnosticLastMs, diagnosticLongestMs,
                diagnosticLastProcessed == 0 ? -1 : Environment.TickCount64 - diagnosticLastProcessed, worker.IsCompleted);
        }
    }
}
