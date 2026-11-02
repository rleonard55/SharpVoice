namespace SharpVoice.Core;

public readonly record struct HotkeyCaptureResult(bool Completed, int? Key);

/// <summary>Collect a shortcut until every key is released; Escape cancels.</summary>
public sealed class HotkeyCaptureState
{
    private readonly bool[] down = new bool[256];
    private HotkeyModifiers modifiers;
    private int primary;
    private bool started;
    public HotkeyCaptureResult Process(int key, bool pressed)
    {
        if (key is < 1 or > 255) return default;
        if (key == 0x1B && pressed) return new(true, null);
        down[key] = pressed;
        if (pressed)
        {
            started = true;
            modifiers |= key switch
            {
                0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
                0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
                0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
                0x5B or 0x5C => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };
            if (!HotkeyDefinition.IsModifier(key)) primary = key;
        }
        if (!started || down.Any(x => x)) return default;
        try { return new(true, HotkeyDefinition.Create(primary, modifiers)); }
        catch (ArgumentException) { return new(true, null); }
    }
}
