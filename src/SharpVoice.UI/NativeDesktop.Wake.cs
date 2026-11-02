using System.Globalization;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private WakePreparation wakePreparationState = new("off", 0, null, "Wake Phrases Off");
    public Action? WakePreparationRetryRequested { get; set; }
    public Action? WakePreparationCancelRequested { get; set; }
    public void SetWakePreparation(WakePreparation state)
    {
        wakePreparationState = state;
        RenderWakePreparation(); UpdateTrayTip();
    }
    private unsafe void OpenWakePhrases()
    {
        StartToolsWindow("wake", "Wake Phrases"); Win32.ShowWindow(toolsList, 0);
        Win32.SetWindowTextW(toolsNotice, "Start dictation or Ask AI with your voice, then continue speaking naturally.");
        ToolControl("STATIC", "Local speech detection + your selected Whisper model: " + config.SpeechModel, 0, 40, 112, 760, 26, 4490);
        ToolControl("STATIC", "Wake Activation", 0, 40, 162, 760, 24);
        var combo = ToolControl("COMBOBOX", "", 0x210213, 40, 190, 760, 180, 4480);
        if (Win32.SetWindowSubclass(combo, &ComboProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style wake activation choices.");
        AddComboItem(combo, "Off (Use Hotkeys)"); AddComboItem(combo, "On (Listen for Wake Phrases)");
        Win32.SendMessageW(combo, 0x14E, config.WakePhrasesEnabled ? 1u : 0u, 0); toolFields["WakePhrasesEnabled"] = combo;
        void Edit(string key, string label, string value, int y)
        {
            ToolControl("STATIC", label, 0, 40, y, 760, 24);
            toolFields[key] = ToolControl("EDIT", value, 0x10080, 40, y + 28, 760, 32);
        }
        Edit("AskWakePhrase", "Ask AI Phrase", config.AskWakePhrase, 244);
        Edit("DictationWakePhrase", "Dictation Phrase", config.DictationWakePhrase, 326);
        Edit("WakeSilenceSeconds", "Silence Stop · Seconds (1–10)", config.WakeSilenceSeconds.ToString(CultureInfo.InvariantCulture), 408);
        ToolControl("STATIC", "", 0, 40, 492, 760, 52, 4491);
        ToolControl("msctls_progress32", "Speech Preparation Progress", 0, 40, 548, 760, 12, 4492);
        ToolButton("Retry Preparation", 4482, 40, 586, 224); ToolButton("Cancel Download", 4483, 282, 586, 224); ToolButton("Help", 4484, 576, 586, 224);
        ToolButton("Save Changes", 4481, 24, 692, 180); ToolButton("Cancel", 4205, 640, 692, 168);
        RenderWakePreparation(); FinishToolsWindow();
    }
    private void SaveWakePhrases()
    {
        if (active) throw new InvalidOperationException("Finish dictation before saving wake preferences.");
        if (!int.TryParse(WakeText(toolFields["WakeSilenceSeconds"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) throw new ArgumentException("Enter a silence stop between 1 and 10 seconds.");
        var next = config with
        {
            WakePhrasesEnabled = Win32.SendMessageW(toolFields["WakePhrasesEnabled"], 0x147, 0, 0) == 1,
            AskWakePhrase = WakeText(toolFields["AskWakePhrase"]).Trim(), DictationWakePhrase = WakeText(toolFields["DictationWakePhrase"]).Trim(), WakeSilenceSeconds = seconds
        };
        next.Validate(); ConfigChanged?.Invoke(next); config = next; CloseTools();
    }
    private unsafe string WakeText(nint control)
    {
        var text = new char[4096]; fixed (char* ptr = text) return new string(text, 0, Win32.GetWindowTextW(control, ptr, text.Length));
    }
    private void RenderWakePreparation()
    {
        if (toolsKind != "wake" || toolsWindow == 0) return;
        var state = wakePreparationState; var downloading = state.State is "preparing" or "downloading" or "verifying";
        var percent = state.TotalBytes is > 0 ? (int)Math.Clamp(state.DownloadedBytes * 100 / state.TotalBytes.Value, 0, 100) : 0;
        Win32.SetWindowTextW(Win32.GetDlgItem(toolsWindow, 4491), state.Message + (state.State == "downloading" ? $" {percent}%" : ""));
        var bar = Win32.GetDlgItem(toolsWindow, 4492);
        Win32.SendMessageW(bar, 0x406, 0, 100); Win32.SendMessageW(bar, 0x402, (nuint)percent, 0); Win32.ShowWindow(bar, downloading ? 5 : 0);
        Win32.ShowWindow(Win32.GetDlgItem(toolsWindow, 4483), downloading ? 5 : 0);
        Win32.ShowWindow(Win32.GetDlgItem(toolsWindow, 4482), state.State is "unavailable" or "canceled" ? 5 : 0);
    }
}
