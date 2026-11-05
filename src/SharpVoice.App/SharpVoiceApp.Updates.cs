namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    internal long LastUpdateActivity { get; private set; } = Environment.TickCount64;
    internal bool UpdateIdle => phase == Phase.Idle && !authBusy && !editorSession.Busy && !pendingVoiceStart && !editorHotkey.Pending
        && work.IsCompleted && authWork.IsCompleted && preload.IsCompleted && speechWork.IsCompleted
        && connectedCancellation is null && WakeUpdateIdle;
    private bool WakeUpdateIdle
    {
        get
        {
            lock (wakeGate) return WakeAllowsUpdate(recorder.IsWakeListening, wakeWork.IsCompleted,
                wakeDetector?.IsRecognizing == true, wakeDetector?.Activity?.Silence);
        }
    }
    internal static bool WakeAllowsUpdate(bool listening, bool prepared, bool recognizing, TimeSpan? silence)
        => prepared && !recognizing && (!listening || silence >= TimeSpan.FromSeconds(2));
    internal void QuiesceForUpdate()
    {
        phase = Phase.Cancelling;
        lifetime.Cancel();
        desktop.QuiesceForUpdate();
    }
}
