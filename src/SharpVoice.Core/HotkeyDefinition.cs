namespace SharpVoice.Core;

[Flags]
public enum HotkeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

public static class HotkeyDefinition
{
    public const int ControlAlt = 0;
    public const int ControlAltShift = 0x10700;
    private const int ChordFlag = 0x10000;
    public static int PrimaryKey(int key) => key == ControlAlt ? 0 : key & 255;
    public static HotkeyModifiers Modifiers(int key) => key == ControlAlt ? HotkeyModifiers.Control | HotkeyModifiers.Alt
        : (key & ChordFlag) == 0 ? HotkeyModifiers.None : (HotkeyModifiers)((key >> 8) & 15);
    public static bool AreEquivalent(int first, int second)
        => PrimaryKey(first) == PrimaryKey(second) && Modifiers(first) == Modifiers(second);
    public static bool IsStrictSubset(int subset, int superset)
    {
        if (!IsSupported(subset) || !IsSupported(superset) || AreEquivalent(subset, superset)) return false;
        var required = Modifiers(subset);
        return (required & Modifiers(superset)) == required
            && (PrimaryKey(subset) == 0 || PrimaryKey(subset) == PrimaryKey(superset));
    }
    public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
    public static bool IsSupported(int key)
    {
        if (key == ControlAlt || key is >= 0x70 and <= 0x87) return true;
        if ((key & ~0x10FFF) != 0 || (key & ChordFlag) == 0) return false;
        var modifiers = Modifiers(key);
        var primary = PrimaryKey(key);
        return modifiers != HotkeyModifiers.None && (primary == 0
            ? System.Numerics.BitOperations.PopCount((uint)modifiers) >= 2
            : IsPrimary(primary));
    }
    private static bool IsPrimary(int key) => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
        || key is >= 0x70 and <= 0x87 or 0x20 or 0x0D or 0x09 or 0x08 or >= 0x21 and <= 0x28 or 0x2D or 0x2E or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDE;
    public static int Create(int primary, HotkeyModifiers modifiers)
    {
        if (primary == 0 && modifiers == HotkeyModifiers.None) throw new ArgumentException("No shortcut keys were pressed.");
        if (primary == 0 && modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt)) return ControlAlt;
        var key = modifiers == HotkeyModifiers.None ? primary : ChordFlag | ((int)modifiers << 8) | primary;
        if (!IsSupported(key)) throw new ArgumentException("Use a function key, two modifiers, or modifiers plus a letter, number or navigation key.");
        return key;
    }
    public static string DisplayName(int key)
    {
        if (!IsSupported(key)) throw new ArgumentOutOfRangeException(nameof(key));
        var parts = new List<string>();
        var mods = Modifiers(key);
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        var primary = PrimaryKey(key);
        if (primary != 0) parts.Add(KeyName(primary));
        return string.Join(" + ", parts);
    }
    private static string KeyName(int key) => key switch
    {
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        >= 0x30 and <= 0x5A => ((char)key).ToString(),
        0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab", 0x08 => "Backspace", 0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
        _ => $"Key{key:X2}"
    };
    public static int FromDisplayName(string name)
    {
        var modifiers = HotkeyModifiers.None;
        var primary = 0;
        foreach (var part in name.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = part switch { "Ctrl" => HotkeyModifiers.Control, "Alt" => HotkeyModifiers.Alt, "Shift" => HotkeyModifiers.Shift, "Win" => HotkeyModifiers.Windows, _ => HotkeyModifiers.None };
            if (modifier != HotkeyModifiers.None) { modifiers |= modifier; continue; }
            if (primary != 0) throw new ArgumentException("Choose only one non-modifier key.");
            primary = Enumerable.Range(1, 254).FirstOrDefault(x => IsPrimary(x) && KeyName(x).Equals(part, StringComparison.OrdinalIgnoreCase));
            if (primary == 0) throw new ArgumentException("Unrecognized shortcut key.");
        }
        return Create(primary, modifiers);
    }
}
