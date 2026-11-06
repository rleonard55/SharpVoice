using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using SharpVoice.App;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    public static void RunEditorHotkeyApp(string modelRoot, string gesture)
    {
        var isolated = CreateDesktopW("SharpVoiceHotkeyCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated hotkey desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter isolated hotkey desktop.");
                CheckEditorHotkey(modelRoot, gesture);
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
    }
    private static void CheckEditorHotkey(string modelRoot, string gesture)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-hotkey-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var editorProcess = gesture.StartsWith("antigravity-", StringComparison.Ordinal) ? "antigravity" : "code";
            if (editorProcess == "antigravity") gesture = gesture["antigravity-".Length..];
            var noAi = gesture == "toggle-no-ai";
            if (noAi) gesture = "toggle";
            var config = new AppConfig { Backend = noAi ? "none" : "chatgpt_oauth", Hotkey = 0x77, RecordingGesture = gesture, LiveTranscription = false };
            using var http = new HttpClient(new RejectNetwork()); using var auth = new ChatGptAuthManager(http);
            using var desktop = new NativeDesktop(config); desktop.Initialize(false, false, audioFeedback: false);
            using var drivers = new MicrophoneActivationChecks.Drivers();
            var source = new WindowTarget(1, (uint)(Environment.ProcessId + 1), 1, editorProcess);
            var focused = source; var selections = 0; var inserted = 0;
            var discoveryPath = Path.Combine(root, "editor", "connection.json");
            var environment = new VoiceEnvironment
            {
                CreateRecorder = settings => drivers.Recorder(false, 60), CaptureTarget = () => focused,
                CaptureSelection = (_, _) => { selections++; return Task.FromResult<SelectionSnapshot?>(null); },
                Insert = (_, _, _, _, _, _) => { inserted++; return Task.CompletedTask; },
                ModelDirectory = modelRoot, HistoryPath = Path.Combine(root, "history.bin"), EditorDiscoveryPath = discoveryPath
            };
            using var app = new SharpVoiceApp(config, desktop, http, auth, environment);
            app.Start(null); Wait(app.Preparation, 30); Until(() => app.IsMicrophoneReady && app.UpdateIdle, 10);
            var desktopCaptures = 0;
            foreach (var mode in new[] { VoiceMode.Dictation, VoiceMode.AskAi }.Where(mode => !noAi || mode == VoiceMode.Dictation))
            {
                desktop.VoicePressed!(mode);
                Until(() => selections > 0, 10);
                var fallbackCapture = drivers.Next(); Wait(fallbackCapture, 5);
                Check(drivers.Count == desktopCaptures + 1 && inserted == 0, "Disconnected editor did not start ordinary desktop voice capture.");
                desktop.CancelRequested!(); Wait(fallbackCapture.Result.Closed.Task, 5); Until(() => app.UpdateIdle, 15);
                desktopCaptures = drivers.Count; selections = 0;
            }
            var discovery = JsonSerializer.Deserialize(File.ReadAllText(discoveryPath), EditorJsonContext.Default.EditorDiscovery)!;
            using var client = new NamedPipeClientStream(".", discovery.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            Wait(client.ConnectAsync(deadline.Token), 10); using var wire = new EditorWire(client);
            var received = new ConcurrentQueue<EditorFrame>();
            var reading = Task.Run(async () =>
            {
                try { while (await wire.ReadAsync(deadline.Token) is { } frame) received.Enqueue(frame); }
                catch (Exception) when (deadline.IsCancellationRequested || !client.IsConnected) { }
            });
            Wait(wire.WriteAsync(new("hello", Token: discovery.Token), deadline.Token), 5);
            EditorFrame Next(string type, string? id = null, string? message = null)
            {
                bool Match(EditorFrame f) => f.Type == type && (id is null || f.Id == id) && (message is null || f.Message == message);
                Until(() => received.Any(Match), 12); return received.First(Match);
            }
            var hello = Next("hello");
            void Send(EditorFrame frame) => Wait(wire.WriteAsync(frame with { HostId = hello.HostId, ClientId = hello.ClientId, Workspace = "fixture" }, deadline.Token), 5);
            Send(new("workspace_sync", "subscribed")); Next("ack", "subscribed");
            foreach (var mode in new[] { VoiceMode.Dictation, VoiceMode.AskAi }.Where(mode => !noAi || mode == VoiceMode.Dictation))
            {
                var count = received.Count(f => f.Type == "hotkey_start");
                desktop.VoicePressed!(mode); Until(() => received.Count(f => f.Type == "hotkey_start") > count, 5);
                var fallback = received.Last(f => f.Type == "hotkey_start");
                Send(new("hotkey_fallback", fallback.Id, Mode: fallback.Mode, State: "no_editor")); Next("ack", fallback.Id);
                Until(() => selections > 0, 10); var nativeCapture = drivers.Next(); Wait(nativeCapture, 5);
                Check(drivers.Count == desktopCaptures + 1, "No-editor response did not use native capture.");
                Send(new("start", fallback.Id, Mode: "dictate", Context: EditorChecks.Context(), State: "hotkey")); Next("error", fallback.Id);
                Check(!nativeCapture.Result.Closed.Task.IsCompleted, "Late editor start canceled desktop fallback.");
                desktop.CancelRequested!(); Wait(nativeCapture.Result.Closed.Task, 5); Until(() => app.UpdateIdle, 15);
                desktopCaptures = drivers.Count; selections = 0;
                Send(new("hotkey_fallback", fallback.Id, Mode: fallback.Mode, State: "no_editor")); Next("ack", fallback.Id); Pump();
                Check(drivers.Count == desktopCaptures && selections == 0, "Repeated fallback revived a finished gesture.");
            }
            foreach (var change in new[] { "release", "control", "window", "error" })
            {
                var count = received.Count(f => f.Type == "hotkey_start");
                desktop.VoicePressed!(VoiceMode.Dictation); Until(() => received.Count(f => f.Type == "hotkey_start") > count, 5);
                var rejected = received.Last(f => f.Type == "hotkey_start");
                if (change == "release") desktop.Released!();
                if (change == "control") focused = source with { Focus = 2 };
                if (change == "window") focused = source with { Handle = 2 };
                Send(new(change == "error" ? "hotkey_error" : "hotkey_fallback", rejected.Id, Mode: "dictate", State: "no_editor")); Next("ack", rejected.Id); Pump();
                Check(drivers.Count == desktopCaptures && selections == 0, "Released, focus-changed or failed handoff fell back into capture.");
                focused = source; Until(() => app.UpdateIdle, 5);
            }
            // Keep subsequent handoff-count checks independent of the fallback fixtures.
            while (received.TryDequeue(out _)) { }

            if (noAi)
            {
                // Use the real hook-owned router: app cancellation must reset its toggle latch.
                // Reflection stays in this fixture; no test-only surface is added to the product.
                using var hook = new GlobalKeyboardHook(config.Hotkey, config.AskHotkey, _ => { }, () => { }, toggle: true);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var field = typeof(NativeDesktop).GetField("hook", flags)!;
                field.SetValue(desktop, hook);
                try
                {
                    var hookRouter = (RecordingGestureRouter)typeof(GlobalKeyboardHook).GetField("gesture", flags)!.GetValue(hook)!;
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var action = hookRouter.Process(new(DualHotkeyTransition.Pressed, VoiceMode.Dictation, false));
                        Check(action.Transition == DualHotkeyTransition.Pressed, "Rejected code request left toggle latched and consumed the next press.");
                        var count = received.Count(f => f.Type == "hotkey_start");
                        desktop.VoicePressed!(action.Mode); Until(() => received.Count(f => f.Type == "hotkey_start") > count, 5);
                        var rejected = received.Last(f => f.Type == "hotkey_start");
                        Send(new("start", rejected.Id, Mode: "dictate", Context: EditorChecks.Context(), State: "hotkey")); Next("error", rejected.Id);
                        hookRouter.Process(new(DualHotkeyTransition.Released, VoiceMode.Dictation, false));
                        Check(drivers.Count == desktopCaptures && selections == 0, "Provider-free code rejection opened desktop capture.");
                    }
                }
                finally { field.SetValue(desktop, null); }
                deadline.Cancel(); client.Dispose(); Wait(reading, 5);
                Console.WriteLine("PASS provider-free desktop fallback and rejected-code toggle retries.");
                return;
            }

            foreach (var (instruction, selected, expected) in new[] { ("Comment", true, "comment"), ("Uncomment!", true, "uncomment"), ("Comment.", false, "noop"), ("Uncomment", false, "noop"), ("Format", true, "format"), ("Format.", false, "format") })
            {
                var shortcutId = Guid.NewGuid().ToString("N");
                var shortcutContext = selected ? EditorChecks.Context() with { SourceText = "class C { }", End = 11, TargetKind = "selection" } : EditorChecks.Context();
                Send(new("start", shortcutId, Mode: "edit", Context: shortcutContext, Instruction: instruction));
                var validation = Next("validate", shortcutId);
                Check(validation.Instruction == instruction && validation.State == "initial", "Exact shortcut was not forwarded for initial-selection validation.");
                Send(new("validated", shortcutId, Context: shortcutContext, Message: validation.Message));
                Check(Next("action", shortcutId).Mode == expected, "Native shortcut called a provider or lost its no-selection guard.");
                Send(new("applying", shortcutId)); Next("apply_ready", shortcutId);
                Send(new("applied", shortcutId)); Next("ack", shortcutId); Until(() => app.UpdateIdle, 5);
            }
            Console.WriteLine("PASS exact native editor shortcuts bypass providers and preserve no-selection no-op over the authenticated bridge.");
            void Start(EditorFrame request) => Send(new("start", request.Id, Mode: "dictate", Context: EditorChecks.Context() with { SourceText = "class C { }", End = 11, TargetKind = "selection" }, State: "hotkey"));
            desktop.VoicePressed!(VoiceMode.Dictation); var released = Next("hotkey_start");
            Check(released.Mode == "dictate" && !app.UpdateIdle, "Custom Dictation did not reserve the editor handoff.");
            desktop.Released!(); Next("hotkey_cancel", released.Id); Start(released); Next("error", released.Id);
            Check(drivers.Count == desktopCaptures && inserted == 0, "Released handoff opened capture or inserted late.");
            Until(() => app.UpdateIdle, 5);
            desktop.VoicePressed!(VoiceMode.Dictation); var stale = Next("hotkey_start", message: null);
            // Match the new request rather than the retained earlier frame.
            Until(() => received.Count(f => f.Type == "hotkey_start") >= 2, 5);
            stale = received.Last(f => f.Type == "hotkey_start");
            focused = source with { Handle = 2, ProcessName = "notepad" }; Next("hotkey_cancel", stale.Id);
            Start(stale); Next("error", stale.Id); focused = source;
            Check(drivers.Count == desktopCaptures && inserted == 0, "Focus change allowed a stale editor start.");
            if (gesture == "hold")
            {
                desktop.VoicePressed!(VoiceMode.Dictation); Until(() => received.Count(f => f.Type == "hotkey_start") >= 3, 5);
                var timed = received.Last(f => f.Type == "hotkey_start"); Next("hotkey_cancel", timed.Id); Start(timed); Next("error", timed.Id);
                Check(drivers.Count == desktopCaptures, "Expired handoff started capture.");
            }
            var requestCount = received.Count(f => f.Type == "hotkey_start");
            var machine = new DualHotkeyStateMachine(config.Hotkey, config.AskHotkey); var router = new RecordingGestureRouter(gesture == "toggle");
            void Key(bool down)
            {
                var action = router.Process(machine.Process(config.Hotkey, down, false));
                if (action.Transition is DualHotkeyTransition.Pressed or DualHotkeyTransition.Promoted) desktop.VoicePressed!(action.Mode);
                if (action.Transition == DualHotkeyTransition.Released) desktop.Released!();
            }
            Key(true); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var accepted = received.Last(f => f.Type == "hotkey_start"); Start(accepted); Next("status", accepted.Id, "recording");
            Check(selections == 0 && drivers.Count == desktopCaptures + 1, "Plugin session used clipboard selection or failed to capture audio.");
            var capture = drivers.Next(); Wait(capture, 5);
            var oldErrors = received.Count(f => f.Type == "error" && f.Id == released.Id);
            Start(released); Until(() => received.Count(f => f.Type == "error" && f.Id == released.Id) > oldErrors, 5);
            Check(!capture.Result.Closed.Task.IsCompleted, "A stale start cancelled the new editor recording.");
            Key(false); Pump();
            if (gesture == "toggle")
            {
                Check(!capture.Result.Closed.Task.IsCompleted && !received.Any(f => f.Type == "status" && f.Id == accepted.Id && f.Message == "processing"), "Toggle key-up stopped recording.");
                Key(true);
            }
            Next("status", accepted.Id, "processing"); desktop.CancelRequested!(); Wait(capture.Result.Closed.Task, 5); Until(() => app.UpdateIdle, 15);
            var askSource = "const answer = 42;";
            var askContext = EditorChecks.Context() with { Name = "test2.js", Language = "javascript", SourceText = askSource, ContextText = askSource, Start = 0, End = askSource.Length, TargetKind = "selection", CursorOffset = null };
            void StartAsk(EditorFrame request) => Send(new("workspace_voice", request.Id, Mode: "ask", State: "hotkey", Context: askContext));
            void WorkspaceState(string state, string id)
            {
                Until(() => received.Any(f => f.Type == "workspace" && f.State == state && f.TurnId == id), 12);
                var frame = received.Last(f => f.Type == "workspace" && f.State == state && f.TurnId == id);
                Check(frame.Context?.Name == "test2.js" && frame.Context.SourceText == askSource && !frame.IncludeFile && frame.Conversation?.Scope.Domain == "editor_ask", "Ask dropped the highlighted text/file identity or leaked it to general conversation.");
            }
            requestCount = received.Count(f => f.Type == "hotkey_start");
            desktop.VoicePressed!(VoiceMode.AskAi); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var releasedAsk = received.Last(f => f.Type == "hotkey_start"); Check(releasedAsk.Mode == "ask", "Ask shortcut was not sent to plugin.");
            desktop.Released!(); Next("hotkey_cancel", releasedAsk.Id); StartAsk(releasedAsk); Next("error", releasedAsk.Id);
            Check(drivers.Count == desktopCaptures + 1 && selections == 0, "Released Ask handoff captured a selection or opened late.");
            // A nested Ask chord replaces a pending Dictation without opening either microphone early.
            requestCount = received.Count(f => f.Type == "hotkey_start");
            desktop.VoicePressed!(VoiceMode.Dictation); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var pendingCode = received.Last(f => f.Type == "hotkey_start"); requestCount++;
            desktop.VoicePressed!(VoiceMode.AskAi); Next("hotkey_cancel", pendingCode.Id); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var pendingAsk = received.Last(f => f.Type == "hotkey_start"); Check(pendingAsk.Mode == "ask", "Pending Dictation did not promote to plugin Ask.");
            Start(pendingCode); Next("error", pendingCode.Id); desktop.Released!(); Next("hotkey_cancel", pendingAsk.Id); StartAsk(pendingAsk); Next("error", pendingAsk.Id);
            // The configured Ask chord follows the actual hold/toggle router and stops with one gesture.
            var askKeys = new DualHotkeyStateMachine(config.Hotkey, config.AskHotkey); var askRouter = new RecordingGestureRouter(gesture == "toggle");
            void AskKeys(bool down)
            {
                foreach (var key in down ? new[] { 0xA2, 0xA4, 0xA0 } : new[] { 0xA0, 0xA4, 0xA2 })
                {
                    var action = askRouter.Process(askKeys.Process(key, down, false));
                    if (action.Transition is DualHotkeyTransition.Pressed or DualHotkeyTransition.Promoted) desktop.VoicePressed!(action.Mode);
                    if (action.Transition == DualHotkeyTransition.Released) desktop.Released!();
                }
            }
            requestCount = received.Count(f => f.Type == "hotkey_start"); AskKeys(true); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var directAsk = received.Last(f => f.Type == "hotkey_start"); Check(directAsk.Mode == "ask", "Configured Ask chord did not target the plugin.");
            StartAsk(directAsk); WorkspaceState("recording", directAsk.Id); var askCapture = drivers.Next(); Wait(askCapture, 5);
            Check(selections == 0, "Plugin Ask used native clipboard selection.");
            StartAsk(directAsk); Next("ack", directAsk.Id); Pump(); Check(!askCapture.Result.Closed.Task.IsCompleted, "Duplicate Ask handoff canceled the accepted recording.");
            AskKeys(false); Pump(); if (gesture == "toggle") { Check(!askCapture.Result.Closed.Task.IsCompleted, "Ask toggle key-up stopped capture."); AskKeys(true); AskKeys(false); }
            WorkspaceState("processing", directAsk.Id); Wait(askCapture.Result.Closed.Task, 5); desktop.CancelRequested!(); Until(() => app.UpdateIdle, 15);
            // Adding Ask after accepted code capture closes that capture and creates a new plugin Ask handoff.
            requestCount = received.Count(f => f.Type == "hotkey_start");
            desktop.VoicePressed!(VoiceMode.Dictation); Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 5);
            var promoted = received.Last(f => f.Type == "hotkey_start"); Start(promoted); Next("status", promoted.Id, "recording");
            var editorCapture = drivers.Next(); Wait(editorCapture, 5); requestCount++;
            desktop.VoicePressed!(VoiceMode.AskAi); Wait(editorCapture.Result.Closed.Task, 5);
            Until(() => received.Count(f => f.Type == "hotkey_start") > requestCount, 15);
            var promotedAsk = received.Last(f => f.Type == "hotkey_start"); Check(promotedAsk.Mode == "ask", "Active code capture promotion left plugin Ask.");
            StartAsk(promotedAsk); WorkspaceState("recording", promotedAsk.Id); var promotedCapture = drivers.Next(); Wait(promotedCapture, 5);
            desktop.CancelRequested!(); Wait(promotedCapture.Result.Closed.Task, 5); Until(() => app.UpdateIdle, 15);
            Check(selections == 0 && inserted == 0, "Plugin Ask captured clipboard selection or inserted source.");
            // General desktop Ask remains available outside supported editors.
            focused = source with { Handle = 2, ProcessName = "notepad" }; desktop.VoicePressed!(VoiceMode.AskAi);
            Until(() => selections == 1, 15); var outsideCapture = drivers.Next(); Wait(outsideCapture, 5);
            desktop.CancelRequested!(); Wait(outsideCapture.Result.Closed.Task, 5); Until(() => app.UpdateIdle, 15);
            deadline.Cancel(); client.Dispose(); Wait(reading, 5);
            Console.WriteLine($"PASS configured F8 {gesture}: plugin handoff, release/focus/expiry rejection, recording stop, read-only plugin Ask handoff/promotion, outside-editor Ask and no clipboard insertion.");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
