using SharpVoice.Core;

namespace SharpVoice.Diagnostics;

public sealed class GuidedAttempt(VoiceMode? expected, AppConfig config)
{
    public WakeSession? Wake { get; private set; }
    public VoiceMode? Expected => expected;
    public VoiceMode? FinalMode { get; private set; }
    private string? finalOutcome;
    public string Complete(string transcript)
    {
        if (Wake is null) return "Missed";
        var resolved = Wake.Resolve(transcript); FinalMode = resolved.Mode;
        return finalOutcome = string.IsNullOrWhiteSpace(resolved.Text) ? "No Request Text"
            : expected is null ? "False Activation" : resolved.Mode == expected ? "Passed" : "Wrong Mode";
    }
    public void Observe(WakeDetection detection)
    {
        if (Wake is null) Wake = new(detection, config);
        else Wake.Confirm(detection);
    }
    public string Outcome => finalOutcome ?? (expected is null ? Wake is null ? "Passed" : "False Activation"
        : Wake is null ? "Missed" : Wake.Mode == expected ? "Passed" : "Wrong Mode");
    public string Result(bool endpointTimedOut) => endpointTimedOut ? "Endpoint Timeout" : Outcome;
}
