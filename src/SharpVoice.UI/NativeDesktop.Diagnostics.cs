using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private readonly Action<string, nint> copyDiagnostics;
    private readonly Action<string> openDiagnosticsUrl;
    private string diagnosticReport = "";

    private void OpenProviderDiagnostics()
    {
        if (active) return;
        StartToolsWindow("diagnostics", "Provider Diagnostics");
        Win32.ShowWindow(toolsList, 0);
        toolFields["DiagnosticExplanation"] = ToolControl("EDIT", "", 0x00A11844, 40, 104, 760, 94);
        ToolControl("STATIC", "Sanitized report preview · Snapshot of recent failures", 0, 40, 210, 760, 24);
        toolFields["DiagnosticReport"] = ToolControl("EDIT", "", 0x00A11844, 40, 240, 760, 236);
        ToolControl("STATIC", "Session memory only: up to 20 failures. Clear or exit removes records; no automatic uploads.\r\nCopy replaces the clipboard with this preview. Clipboard history may retain it.\r\nOpening GitHub contacts GitHub; paste and submit yourself, or share privately with IT.\r\nPublic issues are public and linked to your GitHub account. Sanitized does not mean anonymous.", 0, 40, 490, 760, 150);
        ToolButton("Copy Report", 4801, 40, 646, 220);
        ToolButton("Open GitHub Issue", 4802, 280, 646, 270);
        ToolButton("Clear", 4803, 570, 646, 230);
        ToolButton("Privacy Details", 4804, 40, 692, 220);
        ToolButton("Close", 4205, 640, 692, 160);
        RefreshDiagnosticPreview();
        FinishToolsWindow();
    }
    private void RefreshDiagnosticPreview()
    {
        diagnosticReport = ProviderDiagnostics.Shared.Report();
        Win32.SetWindowTextW(toolFields["DiagnosticReport"], diagnosticReport);
        var latest = ProviderDiagnostics.Shared.Snapshot().LastOrDefault();
        Win32.SetWindowTextW(toolFields["DiagnosticExplanation"], latest is null ? "No provider failures in this session. Run Test Connection to diagnose the selected provider." : $"Latest: {latest.Provider} · {latest.Operation}\r\n{latest.Explanation}");
        Win32.SetWindowTextW(toolsNotice, "ChatGPT and Gemini API failures only. No audio, text content, credentials, internal hostnames or certificate identities are included. Reopen to refresh this snapshot.");
    }
    private void HandleDiagnosticCommand(int id)
    {
        try
        {
            switch (id)
            {
                case 4801:
                    copyDiagnostics(diagnosticReport, toolsWindow);
                    Win32.SetWindowTextW(toolsNotice, "Copied the exact preview. Paste into GitHub or send privately to IT; review any details you add.");
                    break;
                case 4802:
                    openDiagnosticsUrl(ProviderDiagnostics.IssueUrl);
                    Win32.SetWindowTextW(toolsNotice, "Opened GitHub. No report was uploaded. Paste the copied report, review it and submit there yourself.");
                    break;
                case 4803: ProviderDiagnostics.Shared.Clear(); RefreshDiagnosticPreview(); break;
                case 4804: openDiagnosticsUrl(ProviderDiagnostics.PrivacyUrl); break;
            }
        }
        catch (Exception)
        {
            Win32.SetWindowTextW(toolsNotice, id == 4801 ? "Could not copy the report. The preview is still available; retry or select and copy its text manually." : "Could not open the browser. The report is still available. Visit github.com/rleonard55/SharpVoice/issues manually.");
        }
    }
    private static void OpenDiagnosticBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
