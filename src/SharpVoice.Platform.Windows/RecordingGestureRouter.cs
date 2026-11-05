namespace SharpVoice.Platform.Windows;

/// <summary>Maps debounced physical gestures to hold or latched recording transitions.</summary>
public sealed class RecordingGestureRouter(bool toggle)
{
    private bool recording, stoppingGesture;
    private bool externalRecording;
    public void SetExternalRecording(bool active) { externalRecording = active; Reset(); }
    public DualHotkeyEvent Process(DualHotkeyEvent input)
    {
        if (!toggle || externalRecording) return input;
        var transition = DualHotkeyTransition.None;
        if (input.Transition == DualHotkeyTransition.Pressed)
        {
            stoppingGesture = recording;
            recording = !recording;
            transition = recording ? DualHotkeyTransition.Pressed : DualHotkeyTransition.Released;
        }
        else if (input.Transition == DualHotkeyTransition.Promoted && recording && !stoppingGesture) transition = DualHotkeyTransition.Promoted;
        else if (input.Transition == DualHotkeyTransition.Released) stoppingGesture = false;
        return input with { Transition = transition };
    }
    public void SetRecordingActive(bool active) { recording = active; stoppingGesture = !active; }
    public void Reset() => SetRecordingActive(false);
}
