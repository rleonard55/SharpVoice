using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

/// <summary>Pre-rendered monochrome provider marks; native handles live for the UI lifetime.</summary>
internal sealed class ProviderIcons : IDisposable
{
    private readonly Dictionary<(string Provider, bool Dark), nint> icons = [];
    public ProviderIcons()
    {
        try
        {
            foreach (var (provider, asset) in new[] { ("chatgpt_oauth", "openai"), ("claude_code", "claude"), ("gemini_antigravity", "gemini") })
                foreach (var dark in new[] { false, true })
                    icons.Add((provider, dark), FeedbackAssets.LoadIcon(FeedbackAssets.Read($"provider-{asset}-{(dark ? "dark" : "light")}.ico"), 20));
        }
        catch { Dispose(); throw; }
    }
    public nint Find(string provider, bool dark) => icons.GetValueOrDefault((provider == "gemini_api" ? "gemini_antigravity" : provider, dark));
    public void Dispose()
    {
        foreach (var handle in icons.Values) Win32.DestroyIcon(handle);
        icons.Clear();
    }
}
