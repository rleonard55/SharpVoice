namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    public void SetTurnaround(string breakdown) => UpdateHelp("Turnaround", breakdown + "\n\nMeasured from stopping dictation to dispatching insertion; the target app may render on its next frame. AI/network time and your configured insertion delay are included. Only the last successful ordinary insertion is shown, in memory. Fast speech processing is optional; Standard preserves full Whisper context.");
}
