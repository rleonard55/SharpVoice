using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckAnswer(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Text(nint control) { var buffer = new char[4096]; fixed (char* p = buffer) return new string(buffer, 0, Win32.GetWindowTextW(control, p, buffer.Length)); }
        var stops = 0; var replays = 0; var closes = 0;
        desktop.AnswerStopRequested = () => stops++; desktop.AnswerReplayRequested = () => replays++;
        desktop.AnswerClosed = () => closes++;
        var before = GetFocus();
        desktop.ShowAnswer("This is the full answer.\r\nSecond paragraph.", "Fixture Model"); Pump();
        Check(desktop.AnswerWindow != 0 && Win32.IsWindowVisible(desktop.AnswerWindow) != 0, "Smart Ask did not show a response pill.");
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4504) == 0, "Answer pill still has an Insert button.");
        var lastLeft = int.MinValue;
        foreach (var id in new[] { 4514, 4502, 4503, 4505, 4501 })
        {
            Win32.GetWindowRect(Win32.GetDlgItem(desktop.AnswerWindow, id), out var bounds);
            Check(bounds.Left > lastLeft, "Answer actions are not ordered Speech, Replay, Copy, Close, Stop.");
            lastLeft = bounds.Left;
        }
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4514) != 0, "Response pill has no independent speech toggle.");
        var speechChanges = new List<bool>();
        desktop.AnswerSpeechChanged = enabled => speechChanges.Add(enabled);
        Win32.SendMessageW(desktop.AnswerWindow, 0x111, 4514, Win32.GetDlgItem(desktop.AnswerWindow, 4514));
        Check(speechChanges.SequenceEqual(new[] { false }) && Text(Win32.GetDlgItem(desktop.AnswerWindow, 4514)) == "Speech: Off", "Speech toggle did not turn read-aloud off independently.");
        Check(Win32.IsWindowVisible(desktop.AnswerWindow) != 0, "Speech toggle hid the answer.");
        Win32.SendMessageW(desktop.AnswerWindow, 0x111, 4514, Win32.GetDlgItem(desktop.AnswerWindow, 4514));
        Check(speechChanges.SequenceEqual(new[] { false, true }), "Speech toggle did not turn read-aloud on.");
        desktop.ObserveAnswerClick(Win32.GetDlgItem(desktop.AnswerWindow, 4500)); Pump();
        Check(closes == 0 && Win32.IsWindowVisible(desktop.AnswerWindow) != 0, "Clicking inside an answer dismissed it.");
        Check(GetFocus() == before, "Showing an answer stole keyboard focus.");
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4500)).Contains("Second paragraph."), "Answer text was truncated.");
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4509) != 0 && Win32.GetDlgItem(desktop.AnswerWindow, 4510) != 0,
            "Answer popup has no voice reply or text fallback controls.");
        foreach (var id in new[] { 4501, 4502 }) Win32.SendMessageW(desktop.AnswerWindow, 0x111, (nuint)id, Win32.GetDlgItem(desktop.AnswerWindow, id));
        Check(stops == 1 && replays == 1, "Response actions did not route correctly.");
        Check(Win32.GetDlgItem(desktop.AnswerWindow, 4513) != 0, "Answer popup cannot reopen or manage memory-only conversations.");
        var memory = new SharpVoice.Core.ConversationStore();
        var conversation = memory.Active(new("general", "", "fixture"));
        var turn = memory.Begin(conversation.Id, "Give me a recipe"); memory.Complete(turn, "Beef recipe", "Answered", "fixture");
        desktop.SetAnswerConversation(memory.Get(conversation.Id)!);
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4507)).Contains("recipe"), "Conversation title is not visible.");
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
        Win32.SetWindowTextW(Win32.GetDlgItem(desktop.AnswerWindow, 4511), "Unsent reply");
        var editMenu = (nint)0;
        desktop.Post(() =>
        {
            editMenu = AnswerSmokeFindMenu("#32768", null);
            desktop.ObserveAnswerClick(editMenu);
            AnswerSmokeEndMenu();
        });
        var replyInput = Win32.GetDlgItem(desktop.AnswerWindow, 4511);
        Win32.GetWindowRect(replyInput, out var inputRect);
        Win32.SendMessageW(replyInput, 0x7B, (nuint)replyInput, (nint)((inputRect.Top + 4) << 16 | ((inputRect.Left + 4) & 0xFFFF)));
        Pump();
        Check(editMenu != 0, "Native reply context menu did not open.");
        Check(Win32.IsWindowVisible(desktop.AnswerWindow) != 0 && Text(replyInput) == "Unsent reply", "Clicking the reply context menu dismissed the pill or cleared the draft.");
        desktop.SetAnswerStatus("Reading with Windows voice…");
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4506)).Contains("Windows"), "Speech fallback status was hidden.");
        foreach (var theme in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(theme); Win32.SendMessageW(desktop.SettingsHandle, 0x1A, 0, 0); Pump();
            Check(RenderHelp(desktop.AnswerWindow, output is null ? null : output + (theme.Dark ? "-answer-dark.bmp" : "-answer-light.bmp"), 60) == theme.Background, "Answer pill ignored OS theme.");
        }
        Win32.SendMessageW(desktop.AnswerWindow, 0x111, 4505, Win32.GetDlgItem(desktop.AnswerWindow, 4505));
        Check(closes == 1 && Win32.IsWindowVisible(desktop.AnswerWindow) == 0, "Close did not dismiss the answer.");
        desktop.ShowAnswer("Click-away fixture", "fixture");
        desktop.ObserveAnswerClick(desktop.SettingsHandle); Pump();
        Check(closes == 2 && Win32.IsWindowVisible(desktop.AnswerWindow) == 0, "Outside click before pill activation did not close it.");
        desktop.ShowAnswer("Old fixture", "fixture");
        desktop.ObserveAnswerClick(desktop.SettingsHandle);
        desktop.HideAnswer(); desktop.ShowAnswer("New fixture", "fixture"); Pump();
        Check(Win32.IsWindowVisible(desktop.AnswerWindow) != 0, "A stale outside click dismissed a newer answer.");
        desktop.HideAnswer();
        desktop.ShowAnswer("Pending reply fixture", "fixture");
        desktop.ObserveAnswerClick(desktop.SettingsHandle);
        desktop.ShowAnswer("Completed reply in same pill", "fixture"); Pump();
        Check(Win32.IsWindowVisible(desktop.AnswerWindow) == 0, "A reply update invalidated an outside click on the same pill.");
        Check(Win32.GetDlgItem(desktop.SettingsHandle, 119) != 0, "General Settings has no Smart Ask preferences.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 119, Win32.GetDlgItem(desktop.SettingsHandle, 119)); Pump();
        Check(desktop.ToolField("SmartAsk") != 0 && desktop.ToolField("AnswerVoice") != 0 && desktop.ToolField("AnswerSpeechEnabled") != 0 && desktop.ToolField("AnswerVerbosity") != 0, "Smart Ask preferences are incomplete.");
        foreach (var theme in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(theme); Win32.SendMessageW(desktop.SettingsHandle, 0x1A, 0, 0); Pump();
            Check(RenderHelp(desktop.ToolsWindow, output is null ? null : output + (theme.Dark ? "-answer-settings-dark.bmp" : "-answer-settings-light.bmp"), 60) == theme.Background, "Answer settings ignored OS theme.");
        }
        Check(Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x147, 0, 0) == 0, "Smart Ask was enabled by default.");
        Check(Win32.GetDlgItem(desktop.ToolsWindow, 4452) == 0, "Edge voices still require a manual setup button.");
        var originalSave = desktop.ConfigChanged; SharpVoice.Core.AppConfig? saved = null;
        desktop.ConfigChanged = next => saved = next;
        Win32.SendMessageW(desktop.ToolField("AnswerSpeechEnabled"), 0x14E, 0, 0);
        Win32.SendMessageW(desktop.ToolField("AnswerVerbosity"), 0x14E, 2, 0);
        Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x14E, 1, 0); Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x14E, 1, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4451, Win32.GetDlgItem(desktop.ToolsWindow, 4451));
        Check(saved?.SmartAsk == true && saved.AnswerVoice == "en-US-GuyNeural" && !saved.AnswerSpeechEnabled && saved.AnswerVerbosity == "detailed", "Spoken-answer preferences were not saved.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 119, Win32.GetDlgItem(desktop.SettingsHandle, 119));
        Check(Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x147, 0, 0) == 1 && Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x147, 0, 0) == 1, "Preferences did not reopen with the saved choices.");
        Check(Win32.SendMessageW(desktop.ToolField("AnswerSpeechEnabled"), 0x147, 0, 0) == 0 && Win32.SendMessageW(desktop.ToolField("AnswerVerbosity"), 0x147, 0, 0) == 2, "Speech and verbosity preferences did not reopen with saved choices.");
        desktop.ShowAnswer("Silent fixture", "fixture");
        Check(Text(Win32.GetDlgItem(desktop.AnswerWindow, 4514)) == "Speech: Off" && Text(Win32.GetDlgItem(desktop.AnswerWindow, 4506)).Contains("Speech Off"), "Silent answers still promise automatic speech.");
        desktop.HideAnswer();
        Win32.SendMessageW(desktop.ToolField("AnswerSpeechEnabled"), 0x14E, 1, 0);
        Win32.SendMessageW(desktop.ToolField("AnswerVerbosity"), 0x14E, 0, 0);
        Win32.SendMessageW(desktop.ToolField("SmartAsk"), 0x14E, 0, 0); Win32.SendMessageW(desktop.ToolField("AnswerVoice"), 0x14E, 0, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4451, Win32.GetDlgItem(desktop.ToolsWindow, 4451));
        desktop.ConfigChanged = originalSave;
        desktop.AnswerStopRequested = desktop.AnswerReplayRequested = desktop.AnswerClosed = null;
        desktop.AnswerSpeechChanged = null;
        desktop.AnswerVoiceRequested = null; desktop.AnswerReplyRequested = null;
        Console.WriteLine("PASS native response pill text, actions, focus preservation, fallback status, themes and opt-in preferences.");
    }
    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial nint AnswerSmokeFindMenu(string name, string? title);
    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "EndMenu")]
    private static partial int AnswerSmokeEndMenu();
}
