using SharpVoice.Core;

namespace SharpVoice.Platform.Windows;

public enum DualHotkeyTransition { None, Pressed, Promoted, Released }
public readonly record struct DualHotkeyEvent(DualHotkeyTransition Transition, VoiceMode Mode, bool Suppress);

/// <summary>Routes two hold gestures into one latched recording, including nested-chord promotion.</summary>
public sealed class DualHotkeyStateMachine
{
    private readonly int dictationKey, askKey;
    private readonly bool[] downKeys = new bool[256];
    private readonly bool[] reservedKeys = new bool[256];
    private bool active, waitingForRelease;
    private VoiceMode mode;

    public DualHotkeyStateMachine(int dictationKey, int askKey)
    {
        if (!HotkeyDefinition.IsSupported(dictationKey) || !HotkeyDefinition.IsSupported(askKey)) throw new ArgumentException("Unsupported shortcut.");
        if (HotkeyDefinition.AreEquivalent(dictationKey, askKey)) throw new ArgumentException("Shortcuts must be different.");
        if (HotkeyDefinition.IsStrictSubset(askKey, dictationKey)) throw new ArgumentException("Ask AI must include a different or an extra key; otherwise the dictation shortcut cannot start.");
        (this.dictationKey, this.askKey) = (dictationKey, askKey);
    }

    internal void SynchronizeHeldKeys(IEnumerable<int> keys)
    {
        foreach (var key in keys) if (key is > 0 and < 256) downKeys[key] = true;
        active = false;
        waitingForRelease = !GestureReleased();
    }

    public DualHotkeyEvent Process(int virtualKey, bool down, bool injected = false)
    {
        if (injected || virtualKey is < 0 or > 255) return default;
        var dictationPrimary = HotkeyDefinition.PrimaryKey(dictationKey);
        var askPrimary = HotkeyDefinition.PrimaryKey(askKey);
        if (!HotkeyDefinition.IsModifier(virtualKey) && virtualKey != dictationPrimary && virtualKey != askPrimary) return default;
        var wasDown = downKeys[virtualKey];
        downKeys[virtualKey] = down;
        var dictation = Matches(dictationKey);
        var ask = Matches(askKey);
        var transition = DualHotkeyTransition.None;

        if (waitingForRelease)
        {
            // A nested chord must not restart as its modifiers are released in any order.
            if (GestureReleased()) waitingForRelease = false;
        }
        else if (active)
        {
            if (mode == VoiceMode.Dictation && ask)
            {
                mode = VoiceMode.AskAi;
                transition = DualHotkeyTransition.Promoted;
            }
            else if (!(mode == VoiceMode.AskAi ? ask : dictation))
            {
                active = false;
                waitingForRelease = !GestureReleased();
                transition = DualHotkeyTransition.Released;
            }
        }
        else if (down && (ask || dictation))
        {
            active = true;
            mode = ask ? VoiceMode.AskAi : VoiceMode.Dictation;
            transition = DualHotkeyTransition.Pressed;
        }

        // Only suppress a primary up if its down was reserved. Modifiers always pass through.
        var primary = virtualKey != 0 && (virtualKey == dictationPrimary || virtualKey == askPrimary);
        if (primary && down && !wasDown)
            reservedKeys[virtualKey] = (virtualKey == dictationPrimary && (dictation || HotkeyDefinition.Modifiers(dictationKey) == HotkeyModifiers.None))
                || (virtualKey == askPrimary && (ask || HotkeyDefinition.Modifiers(askKey) == HotkeyModifiers.None));
        var suppress = primary && reservedKeys[virtualKey];
        if (primary && !down) reservedKeys[virtualKey] = false;
        return new(transition, mode, suppress);
    }

    private bool Matches(int key)
    {
        var primary = HotkeyDefinition.PrimaryKey(key);
        var modifiers = HotkeyDefinition.Modifiers(key);
        return (!modifiers.HasFlag(HotkeyModifiers.Control) || ControlDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Alt) || AltDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Shift) || ShiftDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Windows) || WindowsDown)
            && (primary == 0 || downKeys[primary]);
    }

    private bool GestureReleased()
    {
        var modifiers = HotkeyDefinition.Modifiers(dictationKey) | HotkeyDefinition.Modifiers(askKey);
        var first = HotkeyDefinition.PrimaryKey(dictationKey);
        var second = HotkeyDefinition.PrimaryKey(askKey);
        return (!modifiers.HasFlag(HotkeyModifiers.Control) || !ControlDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Alt) || !AltDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Shift) || !ShiftDown)
            && (!modifiers.HasFlag(HotkeyModifiers.Windows) || !WindowsDown)
            && (first == 0 || !downKeys[first]) && (second == 0 || !downKeys[second]);
    }

    private bool ControlDown => downKeys[0x11] || downKeys[0xA2] || downKeys[0xA3];
    private bool AltDown => downKeys[0x12] || downKeys[0xA4] || downKeys[0xA5];
    private bool ShiftDown => downKeys[0x10] || downKeys[0xA0] || downKeys[0xA1];
    private bool WindowsDown => downKeys[0x5B] || downKeys[0x5C];
}
