using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckAnswer(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Text(nint control) { var buffer = new char[4096]; fixed (char* p = buffer) return new string(buffer, 0, Win32.GetWindowTextW(control, p, buffer.Length)); }
        var stops = 0; var replays = 0; var inserts = 0; var closes = 0;
        desktop.AnswerStopRequested = () => stops++; desktop.AnswerReplayRequested = () => replays++;
        desktop.AnswerInsertRequested = () => inserts++; desktop.AnswerClosed = () => closes++;
        var before = GetFocus();
        desktop.ShowAnswer("This is the full answer.\r\nSecond paragraph.", "Fixture Model"); Pump();
        Check(desktop.AnswerWindow != 0 && Win32.IsWindowVisible(desktop.AnswerWindow) != 0, "Smart Ask did not show a response pill.");
        Check(GetFocus() == before, "Showing an answer stole keyboard focus.");
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4500)).Contains("Second paragraph."), "Answer text was truncated.");
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4509) != 0 && Win32.GetDlgItem(desktop.AnswerWindow, 4510) != 0,
            "Answer popup has no voice reply or text fallback controls.");
        foreach (var id in new[] { 4501, 4502, 4504 }) Win32.SendMessageW(desktop.AnswerWindow, 0x111, (nuint)id, Win32.GetDlgItem(desktop.AnswerWindow, id));
        Check(stops == 1 && replays == 1 && inserts == 1, "Response actions did not route correctly.");
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4513) != 0, "Answer popup cannot reopen or manage memory-only conversations.");
        var memory = new SharpVoice.Core.ConversationStore();
        var conversation = memory.Active(new("general", "", "fixture"));
        var turn = memory.Begin(conversation.Id, "Give me a recipe"); memory.Complete(turn, "Beef recipe", "Answered", "fixture");
        desktop.SetAnswerConversation(memory.Get(conversation.Id)!);
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4507)).Contains("recipe"), "Conversation title is not visible.");
        desktop.SetAnswerInsertionAvailable(false);
        Win32.SendMessageW(desktop.AnswerWindow, 0x111, 4504, Win32.GetDlgItem(desktop.AnswerWindow, 4504));
        Check(inserts == 1, "Reopened conversation retained an old insertion capability.");
        desktop.SetAnswerReplyState(false);
        Check(ConnectedEnabled(Win32.GetDlgItem(desktop.AnswerWindow, 4504)) == 0, "Reply state restored an invalid historical paste target.");
        desktop.SetAnswerInsertionAvailable(true);
        var voiceReplies = 0; var typedReply = "";
        desktop.AnswerVoiceRequested = () => voiceReplies++;
        desktop.AnswerReplyRequested = text => typedReply = text;
        void Click(int id) => Win32.SendMessageW(desktop.AnswerWindow, 0x111, (nuint)id, Win32.GetDlgItem(desktop.AnswerWindow, id));
        Check(Win32.IsWindowVisible(Win32.GetDlgItem(desktop.AnswerWindow, 4511)) == 0, "Text fallback should stay tucked away until requested.");
        Click(4509); Check(voiceReplies == 1, "Reply by Voice did not request a recording.");
        desktop.SetAnswerReplyState(true, arming: true); Click(4509);
        Check(voiceReplies == 2, "Arming recording cannot be canceled.");
        desktop.SetAnswerReplyState(true, recording: true); Click(4509);
        Check(voiceReplies == 3 && Text(Win32.GetDlgItem(desktop.AnswerWindow, 4509)) == "Stop Recording", "Voice reply cannot stop recording.");
        desktop.SetAnswerReplyState(true); Click(4509); Click(4510);
        Check(voiceReplies == 3 && Win32.IsWindowVisible(Win32.GetDlgItem(desktop.AnswerWindow, 4511)) == 0, "Busy reply allowed duplicate recording or typing.");
        desktop.SetAnswerReplyState(false); Click(4510);
        Check(Win32.IsWindowVisible(Win32.GetDlgItem(desktop.AnswerWindow, 4511)) != 0 && GetFocus() == Win32.GetDlgItem(desktop.AnswerWindow, 4511), "Type Reply did not reveal and focus its input.");
        Win32.SetWindowTextW(Win32.GetDlgItem(desktop.AnswerWindow, 4511), "Dallas"); Click(4512);
        Check(typedReply == "Dallas", "Typed clarification did not route to the conversation.");
        desktop.SetAnswerReplyState(true); typedReply = ""; Click(4512);
        Check(typedReply == "", "Busy popup submitted a duplicate reply.");
        desktop.SetAnswerReplyState(false);
        Win32.SetWindowTextW(Win32.GetDlgItem(desktop.AnswerWindow, 4511), " "); Click(4512);
        Check(typedReply == "", "Blank reply was submitted.");
        desktop.SetAnswerStatus("Reading with Windows voice…");
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4506)).Contains("Windows"), "Speech fallback status was hidden.");
        foreach (var theme in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(theme); Win32.SendMessageW(desktop.SettingsHandle, 0x1A, 0, 0); Pump();
            Check(RenderHelp(desktop.AnswerWindow, output is null ? null : output + (theme.Dark ? "-answer-dark.bmp" : "-answer-light.bmp"), 60) == theme.Background, "Answer pill ignored OS theme.");
        }
        Win32.SendMessageW(desktop.AnswerWindow, 0x111, 4505, Win32.GetDlgItem(desktop.AnswerWindow, 4505));
        Check(closes == 1 && Win32.IsWindowVisible(desktop.AnswerWindow) == 0, "Close did not dismiss the answer.");
        Check(Win32.GetDlgItem(desktop.SettingsHandle, 119) != 0, "General Settings has no Smart Ask preferences.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 119, Win32.GetDlgItem(desktop.SettingsHandle, 119)); Pump();
        Check(desktop.ToolField("SmartAsk") != 0 && desktop.ToolField("AnswerVoice") != 0, "Smart Ask preferences are incomplete.");
        Check(Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x147, 0, 0) == 0, "Smart Ask was enabled by default.");
        Check(Win32.GetDlgItem(desktop.ToolsWindow, 4452) == 0, "Edge voices still require a manual setup button.");
        var originalSave = desktop.ConfigChanged; SharpVoice.Core.AppConfig? saved = null;
        desktop.ConfigChanged = next => saved = next;
        Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x14E, 1, 0); Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x14E, 1, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4451, Win32.GetDlgItem(desktop.ToolsWindow, 4451));
        Check(saved?.SmartAsk == true && saved.AnswerVoice == "en-US-GuyNeural", "Spoken-answer preferences were not saved.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 119, Win32.GetDlgItem(desktop.SettingsHandle, 119));
        Check(Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x147, 0, 0) == 1 && Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x147, 0, 0) == 1, "Preferences did not reopen with the saved choices.");
        Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x14E, 0, 0); Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x14E, 0, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4451, Win32.GetDlgItem(desktop.ToolsWindow, 4451));
        desktop.ConfigChanged = originalSave;
        desktop.AnswerStopRequested = desktop.AnswerReplayRequested = desktop.AnswerInsertRequested = desktop.AnswerClosed = null;
        desktop.AnswerVoiceRequested = null; desktop.AnswerReplyRequested = null;
        Console.WriteLine("PASS native response pill text, actions, focus preservation, fallback status, themes and opt-in preferences.");
    }
}
