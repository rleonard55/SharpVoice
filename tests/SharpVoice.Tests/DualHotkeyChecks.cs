using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;

internal static class DualHotkeyChecks
{
    public static void Run()
    {
        var state = Default();
        Expect(state.Process(0x11, true), DualHotkeyTransition.None);
        Expect(state.Process(0x12, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation);
        Expect(state.Process(0x10, true), DualHotkeyTransition.Promoted, VoiceMode.AskAi);
        Expect(state.Process(0x10, true), DualHotkeyTransition.None);
        Expect(state.Process(0x10, false), DualHotkeyTransition.Released, VoiceMode.AskAi);
        Expect(state.Process(0x10, true), DualHotkeyTransition.None);
        state.Process(0x11, false);
        state.Process(0x12, false);
        state.Process(0x10, false);
        state.Process(0x10, true);
        state.Process(0x12, true);
        Expect(state.Process(0x11, true), DualHotkeyTransition.Pressed, VoiceMode.AskAi);
        Expect(state.Process(0x12, false), DualHotkeyTransition.Released, VoiceMode.AskAi);
        Expect(state.Process(0x12, true), DualHotkeyTransition.None);

        foreach (var release in new[] { 0x10, 0x11, 0x12 })
        {
            state = Default();
            state.Process(0x10, true); state.Process(0x11, true); state.Process(0x12, true);
            Expect(state.Process(release, false), DualHotkeyTransition.Released, VoiceMode.AskAi);
            Expect(state.Process(release, true), DualHotkeyTransition.None);
        }

        state = Default();
        state.Process(0xA2, true);
        Expect(state.Process(0xA4, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation);
        Expect(state.Process(0xA0, true, injected: true), DualHotkeyTransition.None);
        Expect(state.Process(0xA4, false), DualHotkeyTransition.Released, VoiceMode.Dictation);
        state.Process(0xA2, false);
        state.Process(0xA3, true); state.Process(0xA5, true);
        Expect(state.Process(0xA1, true), DualHotkeyTransition.Promoted, VoiceMode.AskAi);

        state = Default();
        state.Process(0xA2, true);
        Expect(state.Process(0xA4, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation);
        Expect(state.Process(0xA3, true), DualHotkeyTransition.None);
        Expect(state.Process(0xA2, false), DualHotkeyTransition.None);
        Expect(state.Process(0xA3, false), DualHotkeyTransition.Released, VoiceMode.Dictation);

        state = new(0x77, 0x78); // Independent F8/F9.
        Expect(state.Process(0x77, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation, true);
        Expect(state.Process(0x77, true), DualHotkeyTransition.None, suppress: true);
        Expect(state.Process(0x78, true), DualHotkeyTransition.Promoted, VoiceMode.AskAi, true);
        Expect(state.Process(0x78, false), DualHotkeyTransition.Released, VoiceMode.AskAi, true);
        Expect(state.Process(0x77, false), DualHotkeyTransition.None, suppress: true);
        Expect(state.Process(0x78, true), DualHotkeyTransition.Pressed, VoiceMode.AskAi, true);
        Expect(state.Process(0x78, false), DualHotkeyTransition.Released, VoiceMode.AskAi, true);

        var ctrlF8 = HotkeyDefinition.Create(0x77, HotkeyModifiers.Control);
        state = new(ctrlF8, HotkeyDefinition.Create(0x78, HotkeyModifiers.Alt));
        Expect(state.Process(0x77, true), DualHotkeyTransition.None); // Forwarded primary down must get forwarded up.
        Expect(state.Process(0x11, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation);
        Expect(state.Process(0x77, false), DualHotkeyTransition.Released, VoiceMode.Dictation);
        state.Process(0x11, false);
        state.Process(0x11, true);
        Expect(state.Process(0x77, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation, true);
        Expect(state.Process(0x11, false), DualHotkeyTransition.Released, VoiceMode.Dictation);
        Expect(state.Process(0x77, false), DualHotkeyTransition.None, suppress: true);

        state = Default();
        state.SynchronizeHeldKeys([0xA2]);
        Expect(state.Process(0xA4, true), DualHotkeyTransition.None);
        Expect(state.Process(0xA2, false), DualHotkeyTransition.None);
        Expect(state.Process(0xA4, false), DualHotkeyTransition.None);
        state.Process(0xA2, true);
        Expect(state.Process(0xA4, true), DualHotkeyTransition.Pressed, VoiceMode.Dictation);

        MustRejectState(HotkeyDefinition.ControlAltShift, HotkeyDefinition.ControlAlt);
        MustRejectState(ctrlF8, 0x77);
        if (!HotkeyDefinition.IsStrictSubset(HotkeyDefinition.ControlAlt, HotkeyDefinition.ControlAltShift)
            || HotkeyDefinition.IsStrictSubset(HotkeyDefinition.ControlAltShift, HotkeyDefinition.ControlAlt)
            || HotkeyDefinition.IsStrictSubset(HotkeyDefinition.ControlAlt, 0x10300))
            throw new Exception("Shortcut strict-subset comparison is incorrect.");
        Configuration();
        Console.WriteLine("PASS dual hold shortcuts: instant dictation, promotion, Ask latching, independent/custom chords, balanced suppression, injected exclusion, and compatible config.");
    }

    private static DualHotkeyStateMachine Default() => new(HotkeyDefinition.ControlAlt, HotkeyDefinition.ControlAltShift);
    private static void Expect(DualHotkeyEvent value, DualHotkeyTransition transition, VoiceMode mode = VoiceMode.Dictation, bool suppress = false)
    {
        if (value.Transition != transition || value.Suppress != suppress || transition != DualHotkeyTransition.None && value.Mode != mode)
            throw new Exception($"Dual shortcut mismatch: expected {transition}/{mode}/suppress={suppress}; got {value}.");
    }

    private static void Configuration()
    {
        if (new AppConfig().AskHotkey != HotkeyDefinition.ControlAltShift) throw new Exception("Wrong Ask shortcut default.");
        if (HotkeyDefinition.DisplayName(HotkeyDefinition.ControlAltShift) != "Ctrl + Alt + Shift") throw new Exception("Wrong Ask shortcut display.");
        MustReject(new AppConfig { AskHotkey = HotkeyDefinition.ControlAlt });
        MustReject(new AppConfig { AskHotkey = 0x10300 }); // Alternate encoding of Ctrl+Alt.
        MustReject(new AppConfig { AskHotkey = -1 });
        MustReject(new AppConfig { Hotkey = HotkeyDefinition.ControlAltShift, AskHotkey = HotkeyDefinition.ControlAlt });
        MustReject(new AppConfig { Hotkey = HotkeyDefinition.Create(0x77, HotkeyModifiers.Control), AskHotkey = 0x77 });
        (new AppConfig()).Validate();
        (new AppConfig { Hotkey = 0x77, AskHotkey = 0x78 }).Validate();
        (new AppConfig { Hotkey = HotkeyDefinition.Create(0x77, HotkeyModifiers.Control), AskHotkey = HotkeyDefinition.Create(0x77, HotkeyModifiers.Alt) }).Validate();
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-dual-hotkey-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new AppConfig { Hotkey = 0x77, AskHotkey = 0x78 };
            ConfigManager.Save(original, path);
            if (ConfigManager.Load(path).AskHotkey != 0x78) throw new Exception("Custom Ask shortcut did not round trip.");
            ConfigManager.Save(original with { AskHotkey = HotkeyDefinition.ControlAlt }, path);
            if (ConfigManager.Load(path).AskHotkey != HotkeyDefinition.ControlAlt) throw new Exception("Explicit zero Ask shortcut was incorrectly migrated.");
            File.WriteAllText(path, LegacyJson(original));
            if (ConfigManager.Load(path).AskHotkey != HotkeyDefinition.ControlAltShift) throw new Exception("Legacy config did not receive Ask shortcut default.");
            File.WriteAllText(path, LegacyJson(original with { Hotkey = HotkeyDefinition.ControlAltShift }));
            var migrated = ConfigManager.Load(path);
            if (migrated.Hotkey != HotkeyDefinition.ControlAltShift || migrated.AskHotkey != 0x78)
                throw new Exception("Legacy Ctrl+Alt+Shift dictation was not preserved with a distinct Ask shortcut.");
            foreach (var primary in new[] { 0x77, 0x78 })
            {
                var oldDictation = HotkeyDefinition.Create(primary, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);
                File.WriteAllText(path, LegacyJson(original with { Hotkey = oldDictation }));
                migrated = ConfigManager.Load(path);
                if (migrated.Hotkey != oldDictation || migrated.AskHotkey != (primary == 0x78 ? 0x79 : 0x78))
                    throw new Exception("Legacy dictation containing the default Ask chord did not receive a usable Ask shortcut.");
            }
            File.WriteAllText(path, JsonSerializer.Serialize(original with { AskHotkey = original.Hotkey }, CoreJsonContext.Default.AppConfig));
            try { ConfigManager.Load(path); }
            catch (ArgumentException) { return; }
            throw new Exception("Explicit duplicate shortcuts in a file were silently migrated.");
        }
        finally { File.Delete(path); }
    }

    private static string LegacyJson(AppConfig config)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(config, CoreJsonContext.Default.AppConfig));
        return "{" + string.Join(",", document.RootElement.EnumerateObject().Where(x => x.Name != "ask_hotkey")
            .Select(x => '"' + x.Name + "\":" + x.Value.GetRawText())) + "}";
    }

    private static void MustRejectState(int dictation, int ask)
    {
        try { _ = new DualHotkeyStateMachine(dictation, ask); }
        catch (ArgumentException) { return; }
        throw new Exception("Ask subset made dictation unreachable.");
    }

    private static void MustReject(AppConfig config)
    {
        try { config.Validate(); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid/duplicate Ask shortcut was accepted.");
    }
}
