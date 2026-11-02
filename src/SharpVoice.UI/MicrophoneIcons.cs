using System.Buffers.Binary;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

internal static class FeedbackAssets
{
    public static byte[] Read(string name)
    {
        using var source = typeof(FeedbackAssets).Assembly.GetManifestResourceStream("SharpVoice.UI.Assets." + name)
            ?? throw new InvalidOperationException("Missing UI feedback asset: " + name);
        using var target = new MemoryStream();
        source.CopyTo(target);
        return target.ToArray();
    }
    public static unsafe nint LoadIcon(byte[] ico, int size)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + i * 16, 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size) continue;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            fixed (byte* bytes = ico)
            {
                var handle = Win32.CreateIconFromResourceEx(bytes + offset, length, 1, 0x00030000, size, size, 0);
                if (handle == 0) throw new InvalidOperationException("Windows could not create the UI icon.");
                return handle;
            }
        }
        throw new InvalidDataException("UI icon has no matching size.");
    }

}

internal readonly record struct MicrophoneIconPair(nint Large, nint Small, nint Tray);

internal sealed class MicrophoneIcons : IDisposable
{
    private readonly Dictionary<MicrophoneState, MicrophoneIconPair> icons = [];
    public MicrophoneIcons()
    {
        try
        {
            foreach (var (state, name) in new[] { (MicrophoneState.Neutral, "neutral"), (MicrophoneState.Recording, "recording"), (MicrophoneState.Error, "error") })
            {
                var data = FeedbackAssets.Read($"microphone-{name}.ico");
                var large = FeedbackAssets.LoadIcon(data, 32);
                try
                {
                    var small = FeedbackAssets.LoadIcon(data, 16);
                    try { icons[state] = new(large, small, FeedbackAssets.LoadIcon(FeedbackAssets.Read($"tray-microphone-{name}.ico"), 16)); }
                    catch { Win32.DestroyIcon(small); throw; }
                }
                catch { Win32.DestroyIcon(large); throw; }
            }
        }
        catch { Dispose(); throw; }
    }

    public MicrophoneIconPair this[MicrophoneState state] => icons[state];

    public void Dispose()
    {
        foreach (var pair in icons.Values) { Win32.DestroyIcon(pair.Large); Win32.DestroyIcon(pair.Small); Win32.DestroyIcon(pair.Tray); }
        icons.Clear();
    }
}
