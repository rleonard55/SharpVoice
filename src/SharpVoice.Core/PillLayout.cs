namespace SharpVoice.Core;

public readonly record struct PillPosition(int X, int Y);

public static class PillLayout
{
    public const int Width = 520, Height = 96;
    public const byte Opacity = 217;
    public const uint PastedDismissMs = 900, OtherDismissMs = 2500;

    public static PillPosition Clamp(PillPosition position, int left, int top, int right, int bottom) => new(
        (int)Math.Clamp((long)position.X, left, Math.Max((long)left, (long)right - Width)),
        (int)Math.Clamp((long)position.Y, top, Math.Max((long)top, (long)bottom - Height)));
}
