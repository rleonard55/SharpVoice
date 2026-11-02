namespace SharpVoice.Core;

public enum MicrophoneState { Neutral, Recording, Error }
public enum FeedbackCue { None, RecordingStart, RecordingStop, Warning, InsertionSuccess }

/// <summary>Recording state is independent of the HUD's transcription/refinement activity.</summary>
public sealed class RecordingFeedback
{
    public MicrophoneState State { get; private set; }
    private bool recording;

    public FeedbackCue BeginRecording()
    {
        if (recording) return FeedbackCue.None;
        recording = true;
        State = MicrophoneState.Recording;
        return FeedbackCue.RecordingStart;
    }

    public FeedbackCue EndRecording()
    {
        if (!recording) return FeedbackCue.None;
        recording = false;
        if (State == MicrophoneState.Recording) State = MicrophoneState.Neutral;
        return FeedbackCue.RecordingStop;
    }

    public FeedbackCue ReportError()
    {
        State = MicrophoneState.Error;
        return FeedbackCue.Warning;
    }

    public void ClearError()
    {
        State = recording ? MicrophoneState.Recording : MicrophoneState.Neutral;
    }
}
