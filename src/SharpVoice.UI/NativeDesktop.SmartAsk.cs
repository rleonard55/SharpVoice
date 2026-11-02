using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private void OpenSmartAsk()
    {
        StartToolsWindow("smartask", "Smart Ask AI");
        Win32.ShowWindow(toolsList, 0);
        Win32.SetWindowTextW(toolsNotice, "Questions appear in a response pill and are read aloud. Writing and editing requests still insert text.");
        ToolCombo("SmartAsk", "Smart Ask AI", 104, ["Off (Insert Every Result)", "On (Speak Questions)"]);
        ToolCombo("AnswerVoice", "Preferred Edge Voice", 190, AnswerVoices.Labels);
        Win32.SendMessageW(toolFields["SmartAsk"], 0x14E, config.SmartAsk ? 1u : 0u, 0);
        Win32.SendMessageW(toolFields["AnswerVoice"], 0x14E, (nuint)Math.Max(0, Array.IndexOf(AnswerVoices.All, config.AnswerVoice)), 0);
        ToolControl("STATIC", "Edge voices send answer text to Microsoft. If Edge speech is unavailable, SharpVoice uses your installed Windows voice.\r\n\r\nEdge voices are included and prepared automatically. No account or additional setup is required.", 0, 288, 294, 520, 150);
        ToolButton("Save Changes", 4451, 24, 692, 180);
        ToolButton("Cancel", 4205, 640, 692, 168);
        FinishToolsWindow();
    }
    private void SaveSmartAsk()
    {
        if (active) throw new InvalidOperationException("Finish dictation before saving spoken-answer preferences.");
        var index = (int)Win32.SendMessageW(toolFields["AnswerVoice"], 0x147, 0, 0);
        if (index < 0 || index >= AnswerVoices.All.Length) throw new ArgumentException("Choose an Edge voice.");
        var next = config with { SmartAsk = Win32.SendMessageW(toolFields["SmartAsk"], 0x147, 0, 0) == 1, AnswerVoice = AnswerVoices.All[index] };
        next.Validate(); ConfigChanged?.Invoke(next); config = next; CloseTools();
    }
}
