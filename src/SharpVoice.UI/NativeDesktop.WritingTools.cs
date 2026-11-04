using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private nint toolsWindow, toolsList, toolsNotice;
    private string toolsKind = "";
    private readonly Dictionary<string, nint> toolFields = [];
    private readonly List<nint> toolControls = [];
    private readonly List<DictionaryEntry> dictionaryDraft = [];
    private readonly List<VoiceSnippet> snippetDraft = [];
    private readonly List<AppProfile> profileDraft = [];
    private int editingRule = -1;
    internal nint ToolsWindow => toolsWindow;
    internal nint ToolField(string key) => toolFields.GetValueOrDefault(key);
    private readonly Dictionary<nint, int> toolButtons = [];

    private unsafe nint ToolControl(string type, string text, uint style, int x, int y, int width, int height, int id = 0)
    {
        var handle = Create(0, type, text, 0x50000000 | style, x, y, width, height, toolsWindow, id);
        Win32.SendMessageW(handle, 0x30, (nuint)font, 1);
        if (type == "EDIT") Win32.SendMessageW(handle, 0xD3, 3, (8 << 16) | 8);
        InstallControlHover(handle, type);
        Win32.SetWindowTheme(handle, theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
        toolControls.Add(handle);
        return handle;
    }
    private unsafe nint ToolButton(string text, int id, int x, int y, int width)
    {
        var button = ToolControl("BUTTON", text, 0x1000B, x, y, width, 36, id);
        toolButtons[button] = id;
        if (Win32.SetWindowSubclass(button, &ToolButtonProcedure, 3, 0) == 0) throw new InvalidOperationException("Could not initialize writing tool action.");
        return button;
    }
    internal bool HandleToolsInput(in Win32.Msg message)
    {
        if (!toolButtons.TryGetValue(message.Hwnd, out var id) || message.Message is not (0x100 or 0x101) || message.WParam is not (13 or 32)) return false;
        if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0 && HoverIsWindowEnabled(message.Hwnd) != 0 && Win32.IsWindowVisible(message.Hwnd) != 0)
            Win32.SendMessageW(toolsWindow, 0x111, (nuint)id, message.Hwnd);
        return true;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint ToolButtonProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (current is { } self)
        {
            var input = new Win32.Msg { Hwnd = hwnd, Message = message, WParam = wParam, LParam = lParam };
            if (self.HandleToolsInput(input)) return 0;
            if (message == 0x87 && wParam is 13 or 32) return Win32.DefSubclassProc(hwnd, message, wParam, lParam) | 4;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &ToolButtonProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private void ToolEdit(string key, string label, int y, int height = 31, bool multiline = false, bool readOnly = false)
    {
        ToolControl("STATIC", label, 0, 288, y, 520, 24);
        toolFields[key] = ToolControl("EDIT", "", (multiline ? 0x00A11044u : 0x10080u) | (readOnly ? 0x800u : 0), 288, y + 26, 520, height);
    }
    private unsafe void ToolCombo(string key, string label, int y, string[] options)
    {
        ToolControl("STATIC", label, 0, 288, y, 520, 24);
        var combo = ToolControl("COMBOBOX", "", 0x210213, 288, y + 26, 520, 240);
        if (Win32.SetWindowSubclass(combo, &ComboProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style rule choices.");
        foreach (var option in options) AddComboItem(combo, option);
        Win32.SendMessageW(combo, 0x14E, 0, 0);
        toolFields[key] = combo;
    }
    private void StartToolsWindow(string kind, string title)
    {
        if (active) throw new InvalidOperationException("Finish dictation before opening writing tools.");
        CloseTools(); HideHelp(); StopInputPreview();
        toolsKind = kind;
        Win32.GetWindowRect(settingsWindow, out var area);
        toolsWindow = Create(0, "SharpVoice.Native", title, 0x00CA0000, Math.Max(0, area.Left - 60), Math.Max(0, area.Top), 856, 780, settingsWindow, 0);
        var heading = ToolControl("STATIC", title, 0, 24, 20, 790, 32);
        Win32.SendMessageW(heading, 0x30, (nuint)headingFont, 1);
        toolsNotice = ToolControl("STATIC", "", 0, 24, 58, 790, 42, 4100);
        toolsList = ToolControl("LISTBOX", "", 0x00A10101, 24, 104, 240, 476, 4101); // Notify, border, scroll, tabstop.
    }
    private void FinishToolsWindow()
    {
        ApplyTheme(); ApplyMicrophoneIcon();
        Win32.ShowWindow(toolsWindow, 5);
        Win32.SetForegroundWindow(toolsWindow);
    }
    private void OpenWritingTools(string kind)
    {
        var title = kind == "dictionary" ? "Personal Dictionary" : kind == "snippets" ? "Voice Snippets" : "App Profiles";
        StartToolsWindow(kind, title);
        dictionaryDraft.Clear(); dictionaryDraft.AddRange(config.Dictionary);
        snippetDraft.Clear(); snippetDraft.AddRange(config.Snippets);
        profileDraft.Clear(); profileDraft.AddRange(config.Profiles);
        Win32.SetWindowTextW(toolsNotice, kind == "dictionary" ? "Preferred spellings apply to ordinary dictation, including when AI is off." : kind == "snippets" ? "Say the exact trigger to insert its text verbatim. Ask AI and selected edits use your speech normally." : "Match Code.exe or another executable. Configure accounts/models in AI & Models; blank values inherit global settings.");
        if (kind == "profiles")
        {
            ToolEdit("Name", "Profile Name", 104);
            ToolEdit("Process", "Executable Name (e.g. Outlook.exe)", 164);
            ToolCombo("Backend", "AI Provider", 224, ["Use Global Setting", .. choices["Backend"].Select(c => c.Label)]);
            ToolEdit("Model", "AI Model ID (Blank: Inherit / Auto for a Different Provider)", 284);
            ToolCombo("Mode", "Writing Mode", 344, ["Use Global Setting", .. choices["Mode"].Select(c => c.Label)]);
            ToolEdit("Minimum", "Minimum AI Words (Blank: Inherit; 0: Always Refine)", 404);
            ToolEdit("Instructions", "Additional Instructions", 464, 100, multiline: true);
        }
        else
        {
            ToolEdit("Trigger", kind == "dictionary" ? "Spoken Phrase" : "Spoken Trigger (e.g. Insert My Signature)", 104);
            ToolEdit("Value", kind == "dictionary" ? "Preferred Spelling" : "Text to Insert", 180, kind == "dictionary" ? 100 : 300, multiline: true);
        }
        ToolButton("New Entry", 4201, 24, 596, 112);
        ToolButton("Remove", 4202, 148, 596, 112);
        ToolButton("Add / Update Entry", 4203, 288, 636, 230);
        ToolButton("Save Changes", 4204, 24, 692, 180);
        ToolButton("Cancel", 4205, 640, 692, 168);
        RefreshRuleList(); ClearRuleFields(); FinishToolsWindow();
    }
    private void RefreshRuleList()
    {
        Win32.SendMessageW(toolsList, 0x184, 0, 0); // LB_RESETCONTENT
        var labels = toolsKind == "dictionary" ? dictionaryDraft.Select(x => x.Spoken + " → " + x.Preferred)
            : toolsKind == "snippets" ? snippetDraft.Select(x => x.Trigger)
            : profileDraft.Select(x => x.Name + " · " + x.ProcessName);
        foreach (var label in labels) AddToolListItem(label);
    }
    private unsafe void AddToolListItem(string label)
    {
        fixed (char* text = Short(label.Replace('\r', ' ').Replace('\n', ' '), 120)) Win32.SendMessageW(toolsList, 0x180, 0, (nint)text);
    }
    private void ClearRuleFields()
    {
        editingRule = -1;
        foreach (var (key, control) in toolFields)
            if (key is "Backend" or "Mode") Win32.SendMessageW(control, 0x14E, 0, 0);
            else Win32.SetWindowTextW(control, "");
        Win32.SendMessageW(toolsList, 0x186, unchecked((nuint)(nint)(-1)), 0);
    }
    private void SelectRule()
    {
        var index = (int)Win32.SendMessageW(toolsList, 0x188, 0, 0);
        if (index < 0) return;
        editingRule = index;
        void Set(string key, string text) => Win32.SetWindowTextW(toolFields[key], text);
        if (toolsKind == "profiles" && index < profileDraft.Count)
        {
            var profile = profileDraft[index];
            Set("Name", profile.Name); Set("Process", profile.ProcessName); Set("Model", profile.Model); Set("Minimum", profile.MinimumAiWords?.ToString(CultureInfo.InvariantCulture) ?? ""); Set("Instructions", profile.Instructions);
            Win32.SendMessageW(toolFields["Backend"], 0x14E, (nuint)(Array.FindIndex(choices["Backend"], c => c.Id == profile.Backend) + 1), 0);
            Win32.SendMessageW(toolFields["Mode"], 0x14E, (nuint)(Array.FindIndex(choices["Mode"], c => c.Id == profile.Mode) + 1), 0);
        }
        else if (toolsKind == "dictionary" && index < dictionaryDraft.Count) { Set("Trigger", dictionaryDraft[index].Spoken); Set("Value", dictionaryDraft[index].Preferred); }
        else if (toolsKind == "snippets" && index < snippetDraft.Count) { Set("Trigger", snippetDraft[index].Trigger); Set("Value", snippetDraft[index].Text); }
    }
    private void UpdateRule()
    {
        string Get(string key) => Text(toolFields[key]);
        var nextDictionary = dictionaryDraft.ToList(); var nextSnippets = snippetDraft.ToList(); var nextProfiles = profileDraft.ToList();
        if (toolsKind == "dictionary")
        {
            var value = new DictionaryEntry(Get("Trigger").Trim(), Get("Value").Trim());
            if (editingRule >= 0) nextDictionary[editingRule] = value; else nextDictionary.Add(value);
        }
        else if (toolsKind == "snippets")
        {
            var value = new VoiceSnippet(Get("Trigger").Trim(), Get("Value"));
            if (editingRule >= 0) nextSnippets[editingRule] = value; else nextSnippets.Add(value);
        }
        else
        {
            var backendIndex = (int)Win32.SendMessageW(toolFields["Backend"], 0x147, 0, 0);
            var modeIndex = (int)Win32.SendMessageW(toolFields["Mode"], 0x147, 0, 0);
            var value = new AppProfile(Get("Name").Trim(), Get("Process").Trim(), backendIndex <= 0 ? "inherit" : choices["Backend"][backendIndex - 1].Id, Get("Model").Trim(), modeIndex <= 0 ? "inherit" : choices["Mode"][modeIndex - 1].Id, string.IsNullOrWhiteSpace(Get("Minimum")) ? null : int.Parse(Get("Minimum"), CultureInfo.InvariantCulture), Get("Instructions"));
            if (editingRule >= 0) nextProfiles[editingRule] = value; else nextProfiles.Add(value);
        }
        var candidate = config with { Dictionary = nextDictionary.ToArray(), Snippets = nextSnippets.ToArray(), Profiles = nextProfiles.ToArray() };
        candidate.Validate();
        dictionaryDraft.Clear(); dictionaryDraft.AddRange(nextDictionary); snippetDraft.Clear(); snippetDraft.AddRange(nextSnippets); profileDraft.Clear(); profileDraft.AddRange(nextProfiles);
        RefreshRuleList(); ClearRuleFields(); Win32.SetWindowTextW(toolsNotice, "Entry updated in this draft. Save Changes keeps all changes; Cancel discards them.");
    }
    private void SaveRules()
    {
        if (active) throw new InvalidOperationException("Finish dictation before saving writing rules.");
        if (toolFields.Any(field => field.Key is not ("Backend" or "Mode") && Text(field.Value).Length != 0)) UpdateRule();
        var next = config with { Dictionary = dictionaryDraft.ToArray(), Snippets = snippetDraft.ToArray(), Profiles = profileDraft.ToArray() };
        next.Validate(); ConfigChanged?.Invoke(next); config = next; CloseTools();
    }
    private void RemoveRule()
    {
        if (editingRule < 0) return;
        if (toolsKind == "dictionary") dictionaryDraft.RemoveAt(editingRule);
        else if (toolsKind == "snippets") snippetDraft.RemoveAt(editingRule);
        else profileDraft.RemoveAt(editingRule);
        RefreshRuleList(); ClearRuleFields();
    }
    private void CloseTools()
    {
        if (toolsKind == "wake") HideHelp();
        CancelConnectedDiscovery();
        CancelPluginOperation();
        CancelHistoryRetry(); ClearControlHover();
        if (toolsWindow != 0) { var old = toolsWindow; toolsWindow = 0; Win32.DestroyWindow(old); }
        foreach (var control in toolControls) editableCombos.Remove(control);
        diagnosticReport = "";
        toolControls.Clear(); toolFields.Clear(); toolButtons.Clear(); toolsList = toolsNotice = 0; toolsKind = "";
    }
    private bool HandleToolsMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (hwnd != toolsWindow || hwnd == 0) return false;
        if (message == 0x10) { CloseTools(); return true; }
        if ((message == 0x6 && (wParam & 0xFFFF) == 0) || message is 0x3 or 0x5) ClearControlHover();
        if (message != 0x111) return false;
        var id = (int)(wParam & 0xFFFF); var notification = wParam >> 16;
        try
        {
            if (id == 4101 && notification == 1) { if (toolsKind == "plugins") RequestPlugin(install: false); else if (toolsKind == "connected") SelectConnectedTool(); else if (toolsKind == "history") SelectHistory(); else if (toolsKind is not ("smartask" or "diagnostics")) SelectRule(); }
            if (notification == 0) switch (id)
            {
                case 2: case 4205: CloseTools(); break;
                case 4201: ClearRuleFields(); break;
                case 4202: RemoveRule(); break;
                case 4203: UpdateRule(); break;
                case 4204: SaveRules(); break;
                case 4401: if (toolsKind == "plugins") RequestPlugin(install: true); break;
                case 4402: if (toolsKind == "plugins") RequestPlugin(install: false); break;
                case 4491: if (toolsKind == "gemini-key") SaveGeminiApiKey(); break;
                case 4492: if (toolsKind == "gemini-key") RemoveGeminiApiKey(); break;
                case 4471: if (toolsKind == "connected") DiscoverConnected(); break;
                case 4472: if (toolsKind == "connected") ToggleConnectedTool(); break;
                case 4473: if (toolsKind == "connected") { connectedAllowed.Clear(); RenderConnectedCatalog(); } break;
                case 4474: if (toolsKind == "connected") SaveConnectedSettings(); break;
                case 4476: if (toolsKind == "connected") AllowAllConnectedTools(); break;
                case 4477: if (toolsKind == "connected") ToggleUnavailableConnectedTools(); break;
                case 4475: if (toolsKind == "connected") ConnectedAccount(); break;
                case 4451: if (toolsKind == "smartask") SaveSmartAsk(); break;
                case 4701: if (toolsKind == "updates") SaveUpdates(); break;
                case 4702: if (toolsKind == "updates") UpdateCheckRequested?.Invoke(); break;
                case 4703: if (toolsKind == "updates") UpdateDownloadRequested?.Invoke(); break;
                case 4704: if (toolsKind == "updates") UpdateInstallRequested?.Invoke(); break;
                case 4705: if (toolsKind == "updates") ToggleUpdateDetails(); break;
                case 4481: if (toolsKind == "wake") SaveWakePhrases(); break;
                case 4482: if (toolsKind == "wake") WakePreparationRetryRequested?.Invoke(); break;
                case 4483: if (toolsKind == "wake") WakePreparationCancelRequested?.Invoke(); break;
                case 4484: if (toolsKind == "wake") ToggleHelp(2121); break;
                default: if (toolsKind == "history") HandleHistoryCommand(id); else if (toolsKind == "diagnostics") HandleDiagnosticCommand(id); break;
            }
        }
        catch (Exception ex) { Win32.SetWindowTextW(toolsNotice, Short(ex.Message, 190)); }
        return true;
    }
}
