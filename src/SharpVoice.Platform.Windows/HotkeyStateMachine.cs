using SharpVoice.Core;

namespace SharpVoice.Platform.Windows;

public enum HotkeyTransition { None, Pressed, Released }
public readonly record struct HotkeyEvent(HotkeyTransition Transition, bool Suppress);

/// <summary>Pure key-event state, separated from the Win32 hook callback.</summary>
public sealed class HotkeyStateMachine(int key)
{
    private bool held;
    private bool reservedPrimary;
    private readonly bool[] downKeys = new bool[256];
    public HotkeyEvent Process(int virtualKey, bool down, bool injected = false)
    {
        if (injected || virtualKey is < 0 or > 255) return default;
        var primary = HotkeyDefinition.PrimaryKey(key);
        if (virtualKey != primary && !HotkeyDefinition.IsModifier(virtualKey)) return default;
        var wasDown = downKeys[virtualKey];
        downKeys[virtualKey] = down;
        var modifiers = HotkeyDefinition.Modifiers(key);
        var next = (!modifiers.HasFlag(HotkeyModifiers.Control) || downKeys[0x11] || downKeys[0xA2] || downKeys[0xA3])
            && (!modifiers.HasFlag(HotkeyModifiers.Alt) || downKeys[0x12] || downKeys[0xA4] || downKeys[0xA5])
            && (!modifiers.HasFlag(HotkeyModifiers.Shift) || downKeys[0x10] || downKeys[0xA0] || downKeys[0xA1])
            && (!modifiers.HasFlag(HotkeyModifiers.Windows) || downKeys[0x5B] || downKeys[0x5C])
            && (primary == 0 || downKeys[primary]);
        var transition = next == held ? HotkeyTransition.None : next ? HotkeyTransition.Pressed : HotkeyTransition.Released;
        held = next;
        // Forward modifier down/up pairs intact, so normal Ctrl/Alt use cannot leave
        // the target application with a stuck modifier. F-key hold events are reserved.
        if (virtualKey == primary && down && !wasDown) reservedPrimary = next || modifiers == HotkeyModifiers.None;
        var suppress = primary != 0 && virtualKey == primary && reservedPrimary;
        if (virtualKey == primary && !down) reservedPrimary = false;
        return new(transition, suppress);
    }
}
