namespace SharpVoice.Core;

public readonly record struct WakeConditions(bool Enabled, bool Ready, bool Paused, bool SpeechSuspended,
    bool AuthBusy, bool EditorBusy, bool Idle, bool Confirming, long Now, long RearmAt)
{
    public bool CanListen => Enabled && Ready && !Paused && !SpeechSuspended && !AuthBusy && !EditorBusy && (Idle || Confirming) && Now >= RearmAt;
    public bool CanActivate => CanListen && Idle;
}
