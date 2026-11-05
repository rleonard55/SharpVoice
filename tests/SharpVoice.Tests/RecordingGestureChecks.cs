using SharpVoice.Core;
using SharpVoice.Platform.Windows;

internal static class RecordingGestureChecks
{
    public static Task WakeRouting()
    {
        foreach (var toggle in new[] { false, true }) foreach (var mode in new[] { VoiceMode.Dictation, VoiceMode.AskAi })
        {
            var keys = new DualHotkeyStateMachine(HotkeyDefinition.ControlAlt, HotkeyDefinition.ControlAltShift);
            var router = new RecordingGestureRouter(toggle); router.SetExternalRecording(true);
            var session = new WakeSession(new(mode, 0, 1), new AppConfig()); var stops = 0;
            void Key(int key, bool down)
            {
                var ev = router.Process(keys.Process(key, down, false));
                if (ev.Transition is DualHotkeyTransition.Pressed or DualHotkeyTransition.Promoted && session.HandlePress(ev.Mode)) stops++;
            }
            // For Ask AI, the nested Dictation press must not swallow Shift promotion.
            if (mode == VoiceMode.AskAi) { Key(0xA2,true); Key(0xA4,true); Key(0xA4,false); Key(0xA2,false); }
            else { var wrong = router.Process(new(DualHotkeyTransition.Pressed, VoiceMode.AskAi, false)); session.HandlePress(wrong.Mode); router.Process(new(DualHotkeyTransition.Released, VoiceMode.AskAi,false)); }
            router.Reset();
            Key(0xA2,true); Key(0xA4,true); if (mode == VoiceMode.AskAi) Key(0xA0,true);
            if (stops != 1) throw new Exception("Matching wake stop was lost after another shortcut.");
            router.SetExternalRecording(false); router.Reset();
        }
        return Task.CompletedTask;
    }
    public static Task ResumedToggle()
    {
        var toggle = new RecordingGestureRouter(true);
        toggle.Process(new(DualHotkeyTransition.Pressed, VoiceMode.Dictation, false));
        toggle.Process(new(DualHotkeyTransition.Promoted, VoiceMode.AskAi, false));
        toggle.SetRecordingActive(false); // Editor capture is cancelled before general Ask resumes.
        toggle.SetRecordingActive(true); // Native microphone readiness synchronizes the toggle again.
        if (toggle.Process(new(DualHotkeyTransition.Released, VoiceMode.AskAi, false)).Transition != DualHotkeyTransition.None
            || toggle.Process(new(DualHotkeyTransition.Pressed, VoiceMode.AskAi, false)).Transition != DualHotkeyTransition.Released)
            throw new Exception("Resumed general Ask needs more than one press to stop.");
        return Task.CompletedTask;
    }
    public static Task Routing()
    {
        static void Check(bool value, string why) { if (!value) throw new Exception(why); }
        var state = new RecordingGestureRouter(true);
        Check(state.Process(new(DualHotkeyTransition.Pressed, VoiceMode.Dictation, false)).Transition == DualHotkeyTransition.Pressed, "First toggle did not start.");
        Check(state.Process(new(DualHotkeyTransition.Promoted, VoiceMode.AskAi, false)).Transition == DualHotkeyTransition.Promoted, "Nested Ask promotion failed.");
        Check(state.Process(new(DualHotkeyTransition.Released, VoiceMode.AskAi, false)).Transition == DualHotkeyTransition.None, "Key-up stopped toggle recording.");
        Check(state.Process(new(DualHotkeyTransition.Pressed, VoiceMode.Dictation, false)).Transition == DualHotkeyTransition.Released, "Second toggle did not stop.");
        Check(state.Process(new(DualHotkeyTransition.Promoted, VoiceMode.AskAi, false)).Transition == DualHotkeyTransition.None, "Stop gesture restarted Ask AI.");
        state.Process(new(DualHotkeyTransition.Released, VoiceMode.AskAi, false));
        state.Process(new(DualHotkeyTransition.Pressed, VoiceMode.Dictation, false)); state.Reset();
        Check(state.Process(new(DualHotkeyTransition.Promoted, VoiceMode.AskAi, false)).Transition == DualHotkeyTransition.None, "Canceled gesture was promoted.");
        state.Process(new(DualHotkeyTransition.Released, VoiceMode.AskAi, false));
        Check(state.Process(new(DualHotkeyTransition.Pressed, VoiceMode.AskAi, false)).Transition == DualHotkeyTransition.Pressed, "Rejected/canceled recording left the toggle latched.");
        var hold = new RecordingGestureRouter(false);
        var release = new DualHotkeyEvent(DualHotkeyTransition.Released, VoiceMode.Dictation, true);
        Check(hold.Process(release) == release, "Hold mode behavior changed.");
        var keys = new DualHotkeyStateMachine(HotkeyDefinition.ControlAlt, HotkeyDefinition.ControlAltShift);
        var router = new RecordingGestureRouter(true); var starts = 0;
        foreach (var (key, down, injected) in new[] { (0xA2,true,false),(0xA4,true,false),(0xA4,true,false),(0xA4,true,true),(0xA4,false,false),(0xA2,false,false) })
            if (router.Process(keys.Process(key,down,injected)).Transition == DualHotkeyTransition.Pressed) starts++;
        Check(starts == 1, "Repeated/injected keys changed the toggle latch.");
        return Task.CompletedTask;
    }
    public static Task Silence()
    {
        var state = new SilenceStopPolicy(3);
        if (state.Observe(-20, -65, TimeSpan.FromSeconds(1)) || state.Observe(-90,-65,TimeSpan.FromSeconds(3.9)) || !state.Observe(-90,-65,TimeSpan.FromSeconds(4))) throw new Exception("Silence threshold timing failed.");
        if (new SilenceStopPolicy(0).Observe(-100,-65,TimeSpan.FromHours(1))) throw new Exception("Disabled auto-stop fired.");
        if (new SilenceStopPolicy(3).Observe(float.NaN,-65,TimeSpan.FromSeconds(4))) throw new Exception("Unavailable microphone triggered auto-stop.");
        return Task.CompletedTask;
    }
}
