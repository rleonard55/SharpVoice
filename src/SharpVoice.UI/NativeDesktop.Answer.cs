using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Core;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    public Action? AnswerReplayRequested { get; set; }
    public Action? AnswerStopRequested { get; set; }
    public Action? AnswerInsertRequested { get; set; }
    public Action? AnswerClosed { get; set; }
    public Action? AnswerVoiceRequested { get; set; }
    public Action<string>? AnswerReplyRequested { get; set; }
    public Func<ConversationView[]>? ReadConversations { get; set; }
    public Action? NewConversationRequested { get; set; }
    public Action<string>? ConversationSelected { get; set; }
    public Action<string>? DeleteConversationRequested { get; set; }
    public Action? ClearConversationsRequested { get; set; }
    private string answerConversationId = "";
    private bool answerInsertionAvailable = true;
    private nint answerWindow, answerText, answerModel, answerStatus, answerInput, answerVoice, answerSend;
    private string answerContent = "";
    private readonly List<nint> answerControls = [];
    private readonly Dictionary<nint, int> answerButtons = [];
    internal nint AnswerWindow => answerWindow;
    public nint ResponseHandle => answerWindow;
    public unsafe void ShowAnswer(string text, string model)
    {
        if (answerWindow == 0)
        {
            Win32.SystemParametersInfoW(0x30, 0, out var work, 0);
            answerWindow = Create(0x88, "SharpVoice.Native", "SharpVoice · Answer", 0x80000000, Math.Max(work.Left, (work.Right + work.Left - 620) / 2), Math.Max(work.Top, work.Bottom - 510), 620, 434, 0, 0);
            var region = CreateRoundRectRgn(0, 0, 621, 435, 44, 44);
            if (region != 0 && SetWindowRgn(answerWindow, region, 1) == 0) Win32.DeleteObject(region);
            nint Item(string kind, string label, uint style, int x, int y, int width, int height, int id)
            {
                var control = Create(0, kind, label, 0x50000000 | style, x, y, width, height, answerWindow, id);
                Win32.SendMessageW(control, 0x30, (nuint)font, 1); answerControls.Add(control); InstallControlHover(control, kind);
                if (kind == "BUTTON")
                {
                    answerButtons[control] = id;
                    if (Win32.SetWindowSubclass(control, &AnswerButtonProcedure, 4, 0) == 0) throw new InvalidOperationException("Could not initialize answer controls.");
                }
                return control;
            }
            var heading = Item("STATIC", "Answer", 0, 24, 16, 380, 26, 4507); Win32.SendMessageW(heading, 0x30, (nuint)hudTitleFont, 1);
            answerModel = Item("STATIC", "", 2, 420, 19, 174, 20, 4508); Win32.SendMessageW(answerModel, 0x30, (nuint)hudModelFont, 1);
            answerText = Item("EDIT", "", 0x00A11844, 24, 55, 572, 180, 4500);
            Win32.SendMessageW(answerText, 0xD3, 3, (8 << 16) | 8);
            answerStatus = Item("STATIC", "", 0, 24, 243, 572, 24, 4506); Win32.SendMessageW(answerStatus, 0x30, (nuint)hudPreviewFont, 1);
            answerVoice = Item("BUTTON", "Reply by Voice", 0x1000B, 24, 274, 210, 34, 4509);
            Item("BUTTON", "Type Reply", 0x1000B, 246, 274, 164, 34, 4510);
            Item("BUTTON", "Conversations", 0x1000B, 422, 274, 174, 34, 4513);
            answerInput = Item("EDIT", "", 0x00811080, 24, 318, 448, 32, 4511);
            Win32.SendMessageW(answerInput, 0xC5, 4000, 0); Win32.SendMessageW(answerInput, 0xD3, 3, (8 << 16) | 8);
            answerSend = Item("BUTTON", "Send", 0x1000B, 484, 318, 112, 32, 4512);
            var labels = new[] { "Stop", "Replay", "Copy", "Insert", "Close" };
            for (var i = 0; i < labels.Length; i++)
            {
                Item("BUTTON", labels[i], 0x1000B, 24 + i * 116, 374, 108, 34, 4501 + i);
            }
        }
        answerContent = text;
        Win32.SetWindowTextW(answerText, text); Win32.SetWindowTextW(answerModel, model); Win32.SetWindowTextW(answerStatus, "Preparing Speech…");
        Win32.SetWindowTextW(answerInput, ""); SetAnswerReplyState(false); ShowAnswerTyping(false);
        ApplyTheme();
        Win32.ShowWindow(hudWindow, 0); Win32.ShowWindow(answerWindow, 4); // Display without activating; an explicit click can focus controls.
    }
    public void SetAnswerStatus(string text) { if (answerStatus != 0) Win32.SetWindowTextW(answerStatus, text); }
    public void SetAnswerConversation(ConversationView view)
    {
        answerConversationId = view.Id;
        if (answerWindow != 0) Win32.SetWindowTextW(Win32.GetDlgItem(answerWindow, 4507), Short(view.Title, 35));
        if (answerWindow != 0) Win32.SetWindowTextW(answerWindow, "SharpVoice · " + view.Title);
    }
    public void SetAnswerInsertionAvailable(bool available)
    {
        answerInsertionAvailable = available;
        if (answerWindow != 0) Win32.EnableWindow(Win32.GetDlgItem(answerWindow, 4504), available ? 1 : 0);
    }
    public void SetAnswerReplyState(bool busy, bool recording = false, bool arming = false)
    {
        if (answerWindow == 0) return;
        Win32.SetWindowTextW(answerVoice, recording ? "Stop Recording" : arming ? "Cancel Recording" : "Reply by Voice");
        Win32.EnableWindow(answerVoice, !busy || recording || arming ? 1 : 0);
        foreach (var id in new[] { 4502, 4504, 4510, 4511, 4512 }) Win32.EnableWindow(Win32.GetDlgItem(answerWindow, id), busy ? 0 : 1);
        if (!answerInsertionAvailable) Win32.EnableWindow(Win32.GetDlgItem(answerWindow, 4504), 0);
    }
    private void ShowAnswerTyping(bool show)
    {
        Win32.ShowWindow(answerInput, show ? 5 : 0); Win32.ShowWindow(answerSend, show ? 5 : 0);
        if (show) { Win32.SetForegroundWindow(answerWindow); AnswerSetFocus(answerInput); }
    }
    public void HideAnswer()
    {
        if (answerWindow == 0 || Win32.IsWindowVisible(answerWindow) == 0) return;
        Win32.ShowWindow(answerWindow, 0); ClearControlHover(); AnswerClosed?.Invoke();
        answerContent = ""; Win32.SetWindowTextW(answerText, "");
        Win32.SetWindowTextW(answerInput, "");
    }
    private bool HandleAnswerMessage(nint hwnd, uint message, nuint wParam)
    {
        if (hwnd == 0 || hwnd != answerWindow) return false;
        if (message == 0x10) { HideAnswer(); return true; }
        if (message != 0x111 || wParam >> 16 != 0) return false;
        switch ((int)(wParam & 0xFFFF))
        {
            case 2: case 4505: HideAnswer(); break;
            case 4501: AnswerStopRequested?.Invoke(); break;
            case 4502: AnswerReplayRequested?.Invoke(); break;
            case 4503: TextInjector.SetClipboardText(answerContent, answerWindow); SetAnswerStatus("Copied"); break;
            case 4504: if (answerInsertionAvailable) AnswerInsertRequested?.Invoke(); break;
            case 4509: if (HoverIsWindowEnabled(answerVoice) != 0) AnswerVoiceRequested?.Invoke(); break;
            case 4510: if (HoverIsWindowEnabled(Win32.GetDlgItem(answerWindow, 4510)) != 0) ShowAnswerTyping(true); break;
            case 4512:
                if (HoverIsWindowEnabled(answerSend) != 0 && !string.IsNullOrWhiteSpace(Text(answerInput))) AnswerReplyRequested?.Invoke(Text(answerInput));
                break;
            case 4513: AnswerConversationMenu(); break;
        }
        return true;
    }
    private void AnswerConversationMenu()
    {
        var views = ReadConversations?.Invoke() ?? [];
        var menu = Win32.CreatePopupMenu(); if (menu == 0) return;
        ClearMenuLabels();
        try
        {
            StyleMenu(menu); AddMenu(menu, 1, "New Conversation");
            AddMenu(menu, 2, "Delete This Conversation"); AddMenu(menu, 3, "Clear All Conversations");
            Win32.AppendMenuW(menu, 0x800, 0, "");
            for (var i = 0; i < views.Length; i++) AddMenu(menu, 100 + i, views[i].Title, selected: views[i].Id == answerConversationId);
            Win32.GetWindowRect(Win32.GetDlgItem(answerWindow, 4513), out var rect);
            var choice = Win32.TrackPopupMenu(menu, 0x102, rect.Left, rect.Bottom, 0, answerWindow, 0);
            if (choice == 1) NewConversationRequested?.Invoke();
            else if (choice == 2 && answerConversationId.Length > 0) DeleteConversationRequested?.Invoke(answerConversationId);
            else if (choice == 3) ClearConversationsRequested?.Invoke();
            else if (choice >= 100 && choice < 100 + views.Length) ConversationSelected?.Invoke(views[choice - 100].Id);
        }
        finally { Win32.DestroyMenu(menu); ClearMenuLabels(); }
    }
    private bool HandleAnswerInput(in Win32.Msg message)
    {
        if (!answerButtons.TryGetValue(message.Hwnd, out var id) || message.Message is not (0x100 or 0x101) || message.WParam is not (13 or 32)) return false;
        if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0 && HoverIsWindowEnabled(message.Hwnd) != 0) Win32.SendMessageW(answerWindow, 0x111, (nuint)id, message.Hwnd);
        return true;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint AnswerButtonProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (current is { } self)
        {
            var input = new Win32.Msg { Hwnd = hwnd, Message = message, WParam = wParam, LParam = lParam };
            if (self.HandleAnswerInput(input)) return 0;
            if (message == 0x87 && wParam is 13 or 32) return Win32.DefSubclassProc(hwnd, message, wParam, lParam) | 4;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &AnswerButtonProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")] private static partial nint AnswerSetFocus(nint control);
}
