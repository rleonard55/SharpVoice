using SharpVoice.Core;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private bool setupPractice;
    private int setupPracticeSession;

    private void InitializeSetup()
    {
        desktop.CanConfigureSetup = () => phase == Phase.Idle && !editorSession.Busy && !authBusy && speechWork.IsCompleted;
        desktop.SetupDismissed = () =>
        {
            var next = config with { SetupFinished = true };
            ConfigManager.Save(next); config = next;
        };
        desktop.SetupPracticeRequested = ToggleSetupPractice;
        desktop.SetupCancelRequested = () => { if (setupPractice) Cancel(); };
    }

    private void ToggleSetupPractice()
    {
        if (setupPractice)
        {
            if (phase == Phase.Recording) Released();
            else if (phase == Phase.Arming) Cancel();
            return;
        }
        if (!desktop.SetupPracticeFocused) throw new InvalidOperationException("Focus the practice step before recording.");
        if (phase != Phase.Idle || authBusy || editorSession.Busy || !speechWork.IsCompleted)
            throw new InvalidOperationException("SharpVoice is getting ready. Please try practice again in a moment.");
        setupPractice = true; setupPracticeSession = desktop.SetupSession;
        try
        {
            desktop.SetSetupPracticeState(true);
            Pressed(VoiceMode.Dictation);
            if (phase is not (Phase.Arming or Phase.Recording)) FinishSetupPractice();
        }
        catch { FinishSetupPractice(); throw; }
    }

    private void FinishSetupPractice()
    {
        if (!setupPractice) return;
        setupPractice = false; desktop.SetSetupPracticeState(false);
    }
}
