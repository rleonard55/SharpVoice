namespace SharpVoice.Core;

public readonly record struct PillPosition(int X, int Y);
public readonly record struct PillWorkArea(int Left, int Top, int Right, int Bottom);

public static class PillLayout
{
    public const int Width = 520, Height = 96;
    public const byte Opacity = 217;
    public const uint PastedDismissMs = 900, OtherDismissMs = 2500;

    public static PillPosition OnMonitor(PillPosition? saved, PillWorkArea source, PillWorkArea target)
    {
        if (saved is not PillPosition position)
            return Clamp(new((int)(target.Left + ((long)target.Right - target.Left - Width) / 2),
                (int)Math.Max(target.Top, (long)target.Bottom - Height - 40)), target.Left, target.Top, target.Right, target.Bottom);
        position = Clamp(position, source.Left, source.Top, source.Right, source.Bottom);
        return new(Map(position.X, source.Left, source.Right, target.Left, target.Right, Width),
            Map(position.Y, source.Top, source.Bottom, target.Top, target.Bottom, Height));
    }

    private static int Map(int value, int start, int end, int targetStart, int targetEnd, int size)
    {
        var available = Math.Max(0L, (long)end - start - size);
        var targetAvailable = Math.Max(0L, (long)targetEnd - targetStart - size);
        var fraction = available == 0 ? 0.5 : ((long)value - start) / (double)available;
        return (int)(targetStart + Math.Round(fraction * targetAvailable));
    }

    public static PillPosition Clamp(PillPosition position, int left, int top, int right, int bottom) => new(
        (int)Math.Clamp((long)position.X, left, Math.Max((long)left, (long)right - Width)),
        (int)Math.Clamp((long)position.Y, top, Math.Max((long)top, (long)bottom - Height)));
}
