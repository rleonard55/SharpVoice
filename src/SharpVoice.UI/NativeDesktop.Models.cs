using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private const string AutomaticModelLabel = "Auto (Recommended)";
    private readonly Dictionary<string, string> modelLabels = [];
    private readonly Dictionary<string, string> modelDescriptions = [];
    private nint modelHint, refreshModelsButton;
    private bool modelWasUsed;
    private string resolvedBackend = "", resolvedModel = "";
    private string modelCatalogNote = "Sign in to load your available ChatGPT models.";
    public Action<string>? ModelsRefreshRequested { get; set; }

    private string SelectedModelId()
    {
        var text = Text(fields["RefinementModel"]).Trim();
        return modelLabels.GetValueOrDefault(text, text);
    }

    public void SetAvailableModels(IReadOnlyList<(string Id, string Label, string Description)> models, string? note = null)
    {
        if (SelectedChoice("Backend") != "chatgpt_oauth") return;
        var selected = SelectedModelId();
        modelCatalogNote = note ?? "Auto selects the lowest known credit cost available. Speed varies.\nOr type an exact model ID from your account.";
        modelLabels.Clear(); modelDescriptions.Clear();
        modelLabels[AutomaticModelLabel] = "auto";
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        AddComboItem(combo, AutomaticModelLabel);
        foreach (var model in models)
        {
            var label = model.Label.Replace(" · Recommended", " (Recommended)", StringComparison.Ordinal);
            modelLabels[label] = model.Id;
            modelDescriptions[model.Id] = model.Description;
            AddComboItem(combo, label);
        }
        SetModelSelection(selected);
        RefreshModelHint();
    }

    private void SetModelSelection(string id)
    {
        var label = modelLabels.FirstOrDefault(x => x.Value == id).Key ?? id;
        Win32.SetWindowTextW(fields["RefinementModel"], label);
    }

    public void SetResolvedModel(string backend, string model, bool used = true)
    {
        if (!used && modelWasUsed && resolvedBackend == backend) return;
        resolvedBackend = backend; resolvedModel = model; modelWasUsed = used;
        RefreshModelHint();
        Win32.InvalidateRect(hudWindow, 0, 0);
    }

    private string claudeStatusNote = "";
    public void SetClaudeStatus(string note) { claudeStatusNote = note; RefreshModelHint(); }
    private void SetClaudeModels()
    {
        var selected = SelectedModelId();
        modelLabels.Clear(); modelDescriptions.Clear();
        modelLabels[AutomaticModelLabel] = "auto";
        modelLabels["Haiku (Recommended)"] = "haiku";
        modelLabels["Sonnet"] = "sonnet";
        modelLabels["Opus"] = "opus";
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        foreach (var label in modelLabels.Keys) AddComboItem(combo, label);
        SetModelSelection(selected);
        RefreshModelHint();
    }

    private void SetTranscriptionOnlyModel()
    {
        var selected = SelectedModelId();
        modelLabels.Clear(); modelDescriptions.Clear();
        modelLabels["Not Used (Transcription Only)"] = selected;
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        AddComboItem(combo, "Not Used (Transcription Only)");
        SetModelSelection(selected);
        RefreshModelHint();
    }

    private void SetEmbeddedModels()
    {
        var selected = SelectedModelId();
        modelLabels.Clear(); modelDescriptions.Clear();
        modelLabels[AutomaticModelLabel] = "auto";
        foreach (var model in LocalAiModels.All) modelLabels[model.Label] = model.Id;
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        foreach (var label in modelLabels.Keys) AddComboItem(combo, label);
        SetModelSelection(selected);
        RefreshModelHint();
    }

    private string googleStatusNote = "Google account is managed by Antigravity.";
    public void SetGoogleStatus(string note) { googleStatusNote = note; RefreshModelHint(); }
    private void SetGoogleModels()
    {
        var selected = SelectedModelId();
        modelLabels.Clear(); modelDescriptions.Clear();
        modelLabels[AutomaticModelLabel] = "auto";
        modelLabels["Gemini 3.8 Flash Medium (Recommended)"] = "gemini-3.8-flash-medium";
        modelLabels["Gemini 4 Argon Medium"] = "gemini-4-argon-medium";
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        foreach (var label in modelLabels.Keys) AddComboItem(combo, label);
        SetModelSelection(selected);
        RefreshModelHint();
    }

    private void SetGoogleApiModels()
    {
        var selected=SelectedModelId();modelLabels.Clear();modelDescriptions.Clear();
        modelLabels[AutomaticModelLabel]="auto";
        modelLabels["Gemini 3.1 Flash-Lite (Recommended, Economy)"]="gemini-3.1-flash-lite";
        modelLabels["Gemini 3.5 Flash-Lite (Newer)"]="gemini-3.5-flash-lite";
        modelLabels["Gemini 3.8 Flash (More Capable)"]="gemini-3.8-flash";
        var combo=fields["RefinementModel"];Win32.SendMessageW(combo,0x14B,0,0);
        foreach(var label in modelLabels.Keys)AddComboItem(combo,label);
        SetModelSelection(selected);RefreshModelHint();
    }

    internal string ModelSummary => cleanupSkipped ? $"Whisper {HudConfig.SpeechModel} · AI cleanup skipped" : HudConfig.Backend switch
    {
        "none" => $"Whisper {HudConfig.SpeechModel} · local transcription",
        var backend => $"Whisper {HudConfig.SpeechModel} · {(backend == "chatgpt_oauth" ? "ChatGPT" : backend == "claude_code" ? "Claude" : backend is "gemini_antigravity" or "gemini_api" ? "Gemini" : "Local AI")} · {(resolvedBackend == backend && resolvedModel.Length != 0 ? resolvedModel : HudConfig.RefinementModel == "auto" ? "auto · resolving model" : HudConfig.RefinementModel)}"
    };

    private void RefreshModelHint()
    {
        if (modelHint == 0) return;
        var backend = SelectedChoice("Backend");
        var id = SelectedModelId();
        var detail = backend == "chatgpt_oauth"
            ? modelDescriptions.GetValueOrDefault(id, modelCatalogNote)
            : backend == "none" ? "No AI cleanup or account needed. Ask AI and selected-text edits require an AI provider."
            : backend == "embedded" ? "Choose a local model, then Download Model to prepare it. Qwen 0.5B is smaller and faster; 1.5B uses more memory and CPU. No sign-in needed."
            : backend == "gemini_api" ? "Uses your Google AI Studio API key. Auto uses Gemini 3.1 Flash-Lite for economy and speed. Manage API Key stores it encrypted for your Windows user, separately from preferences and history. API project quota/billing applies; this does not use your Gemini subscription. Use Test Connection after saving the key. " + (HasGeminiApiKey?.Invoke() == true ? "A key is saved." : "No key is saved yet.")
            : backend == "gemini_antigravity" ? "Requires Antigravity. Auto uses Flash Medium; the CLI may retain history. " + googleStatusNote
            : "Install Claude Code and sign in below. Auto uses Haiku; Claude plan limits apply. " + claudeStatusNote;
        var used = resolvedBackend == backend && resolvedModel.Length != 0 ? (modelWasUsed ? "Last used: " : "Auto selects: ") + resolvedModel + ". " : "";
        Win32.SetWindowTextW(modelHint, used.Length != 0 ? used.TrimEnd(' ', '.') : id != "auto" && backend != "none" ? "Selected Model: " + id : "");
        UpdateHelp("RefinementModel", detail + "\n\nChoose Auto for the recommended economical model, or type an exact model ID from your account. " + used);
        var onModels = Win32.SendMessageW(tabControl, 0x130B, 0, 0) == 1;
        Win32.ShowWindow(retentionNotice, onModels && backend == "gemini_antigravity" ? 5 : 0);
        Win32.ShowWindow(refreshModelsButton, onModels && backend == "chatgpt_oauth" ? 5 : 0);
        RefreshConnectionTestControls();
    }

    private void RequestModelsRefresh() => ModelsRefreshRequested?.Invoke(SelectedChoice("Backend"));
}
