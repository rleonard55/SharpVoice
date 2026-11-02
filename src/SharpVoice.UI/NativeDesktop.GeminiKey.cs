using SharpVoice.Platform.Windows.Native;
using System.Runtime.InteropServices;
namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")] private static partial nint FocusGeminiKey(nint control);
    public Func<bool>? HasGeminiApiKey { get; set; }
    public Action<string>? SaveGeminiApiKeyRequested { get; set; }
    public Action? RemoveGeminiApiKeyRequested { get; set; }
    private void OpenGeminiApiKey()
    {
        if (active || IsAuthenticationBusy?.Invoke() == true) return;
        CancelConnectionTest();
        StartToolsWindow("gemini-key", "Google AI Studio API Key"); Win32.ShowWindow(toolsList, 0);
        Win32.SetWindowTextW(toolsNotice, "Paste your Google AI Studio key below. It is encrypted for your Windows user and kept outside preferences and history.");
        ToolControl("STATIC", "API Key (Enter a New Key to Add or Replace)", 0, 32, 130, 760, 25);
        var entry = ToolControl("EDIT", "", 0x100A0, 32, 162, 760, 36, 4490);
        toolFields["GeminiKey"] = entry;
        Win32.SendMessageW(entry, 0xCC, 0x2022, 0); // EM_SETPASSWORDCHAR
        Win32.SendMessageW(entry, 0xC5, 4096, 0); // EM_SETLIMITTEXT
        ToolControl("STATIC", HasGeminiApiKey?.Invoke() == true ? "A Key Is Saved (Encrypted). Leave the Field Blank to Keep It." : "No API Key Saved Yet.", 0, 32, 220, 760, 28);
        ToolControl("STATIC", "Saving the key does not change your AI provider. Choose Gemini (AI Studio API Key), use Test Connection, then Apply Settings. Google API project quota and billing apply.", 0, 32, 267, 760, 70);
        ToolButton("Save API Key", 4491, 32, 366, 220);
        var remove = ToolButton("Remove Saved Key", 4492, 272, 366, 220);
        Win32.EnableWindow(remove, HasGeminiApiKey?.Invoke() == true ? 1 : 0);
        ToolButton("Cancel", 4205, 640, 692, 168);
        FinishToolsWindow(); FocusGeminiKey(entry);
    }
    private void SaveGeminiApiKey()
    {
        if (active || IsAuthenticationBusy?.Invoke() == true || SaveGeminiApiKeyRequested is null) return;
        var entry = toolFields["GeminiKey"]; var key = Text(entry);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter an API key, or choose Cancel to keep the existing key.");
        SaveGeminiApiKeyRequested(key);
        Win32.SetWindowTextW(entry, ""); CloseTools(); RefreshModelHint();
        SetStatus("Google AI Studio key saved securely. Use Test Connection, then Apply Settings.");
    }
    private void RemoveGeminiApiKey()
    {
        if (active || IsAuthenticationBusy?.Invoke() == true || RemoveGeminiApiKeyRequested is null) return;
        RemoveGeminiApiKeyRequested();
        Win32.SetWindowTextW(toolFields["GeminiKey"], ""); CloseTools(); RefreshModelHint();
        SetStatus("Google AI Studio key removed.");
    }
}
