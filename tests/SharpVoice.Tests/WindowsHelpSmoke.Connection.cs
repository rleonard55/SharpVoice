using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckConnection(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var window = desktop.SettingsHandle;
        nint Item(int id) => Win32.GetDlgItem(window, id);
        string Text(int id)
        {
            var buffer = new char[2048];
            fixed (char* text = buffer) return new string(buffer, 0, Win32.GetWindowTextW(Item(id), text, buffer.Length));
        }
        void Page(int page)
        {
            var tab = Item(1014); Win32.SendMessageW(tab, 0x130C, (nuint)page, 0);
            var notification = new Win32.Notification { Window = tab, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        }
        void Provider(int index)
        {
            Win32.SendMessageW(Item(1002), 0x14E, (nuint)index, 0);
            Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, Item(1002));
        }
        void Model(string text)
        {
            Win32.SetWindowTextW(Item(1003), text);
            Win32.SendMessageW(window, 0x111, (5u << 16) | 1003, Item(1003));
        }
        void Click() => Win32.SendMessageW(window, 0x111, 112, Item(112));
        void Wait(Func<bool> condition)
        {
            for (var i = 0; i < 30 && !condition(); i++) Pump();
            Check(condition(), "Connection UI completion did not arrive.");
        }
        var oldChat = desktop.IsChatGptSignedIn; var oldClaude = desktop.IsClaudeSignedIn;
        var oldKey = desktop.HasGeminiApiKey; var oldLocal = desktop.IsLocalModelPresent;
        var ready = false; var readinessCalls = 0;
        desktop.IsChatGptSignedIn = () => ready; desktop.IsClaudeSignedIn = () => ready;
        desktop.HasGeminiApiKey = () => ready; desktop.IsLocalModelPresent = _ => ready;
        desktop.ConnectionTestRequested = (_, _, _) => { readinessCalls++; return Task.FromResult(new ConnectionTestResult("fixture-ready", TimeSpan.Zero)); };
        Page(1);
        foreach (var provider in new[] { 1, 3, 5, 2 })
        {
            ready = false; Provider(provider); desktop.RefreshAuthentication();
            Check(UpdateControlEnabled(Item(112)) == 0, "Unready provider must disable Test Connection.");
            var count = readinessCalls; Click();
            Check(readinessCalls == count, "A direct command bypassed provider readiness.");
            ready = true; desktop.RefreshAuthentication();
            Check(UpdateControlEnabled(Item(112)) != 0, "Ready provider did not enable Test Connection.");
            Click(); Wait(() => Text(1025).Contains("Connected"));
            Check(readinessCalls == count + 1, "Ready transition did not allow exactly one test.");
        }
        Page(1); Provider(1); Model("unsaved-draft-model");
        var calls = 0; var selected = ""; var model = "";
        CancellationToken token = default;
        var pending = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.ConnectionTestRequested = (backend, id, ct) => { calls++; selected = backend; model = id; token = ct; return pending.Task; };
        desktop.RefreshAuthentication();
        Win32.SetWindowTextW(Item(1021), "invalid unsaved field");
        Check((Win32.SendMessageW(Item(112), 0x87, 13, 0) & 4) != 0, "Test Connection Enter can fall through to Apply Settings.");
        Win32.SendMessageW(Item(112), 0x100, 13, 0);
        Win32.SendMessageW(Item(112), 0x101, 13, 0);
        Check(calls == 1 && selected == "chatgpt_oauth" && model == "unsaved-draft-model" && token.CanBeCanceled, "Test ignored the draft selection or tried to apply unrelated preferences.");
        Check(Text(112) == "Cancel Test" && Text(1025).Contains("Testing"), "Pending test is not visible/cancellable.");
        ready = false; desktop.RefreshAuthentication();
        Check(UpdateControlEnabled(Item(112)) != 0, "Loss of readiness must retain Cancel Test for active work.");
        Click(); Click(); ready = true; desktop.RefreshAuthentication();
        Check(token.IsCancellationRequested && calls == 1, "Cancel or duplicate click started another request.");
        pending.SetResult(new("stale-canceled-model", TimeSpan.FromSeconds(1)));
        Wait(() => Text(112) == "Test Connection");
        Check(!Text(1025).Contains("Connected"), "Canceled request displayed success.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(); Model("changed-draft-model");
        Check(token.IsCancellationRequested, "Changing the model did not cancel the old test.");
        pending.SetResult(new("stale-old-model", TimeSpan.Zero));
        Wait(() => Text(112) == "Test Connection");
        Check(Text(1025) == "", "Old model result survived a draft change.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(); Page(2);
        Check(token.IsCancellationRequested, "Leaving AI settings did not cancel the test.");
        var before = calls; Click(); Check(calls == before, "Hidden test action started a request.");
        pending.SetResult(new("stale-tab-model", TimeSpan.Zero));
        Wait(() => Text(112) == "Test Connection");
        Page(1);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(); desktop.SetActive(true);
        Check(token.IsCancellationRequested, "Dictation start did not cancel the test.");
        pending.SetResult(new("stale-dictation-model", TimeSpan.Zero));
        Wait(() => Text(112) == "Test Connection"); desktop.SetActive(false);
        var authBusy = false; desktop.IsAuthenticationBusy = () => authBusy;
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(); authBusy = true; desktop.RefreshAuthentication();
        Check(token.IsCancellationRequested, "Sign-in work did not cancel the probe.");
        pending.SetResult(new("stale-auth-model", TimeSpan.Zero));
        Wait(() => Text(112) == "Test Connection"); authBusy = false; desktop.RefreshAuthentication();
        foreach (var (index, expected, id) in new[] { (3, "claude_code", "haiku"), (4, "gemini_antigravity", "gemini-3.8-flash-medium"), (2, "embedded", "qwen2.5-0.5b-instruct-q4_k_m"), (1, "chatgpt_oauth", "exact-chatgpt-model") })
        {
            Provider(index); Model(id);
            desktop.ConnectionTestRequested = (backend, actual, ct) =>
            {
                Check(backend == expected && actual == id, "Test routed friendly labels or the applied provider.");
                return Task.FromResult(new ConnectionTestResult("verified-" + id, TimeSpan.FromSeconds(0.3)));
            };
            desktop.RefreshAuthentication(); Click(); Wait(() => Text(1025).Contains("Connected"));
            Check(Text(1025).Contains("verified-"), "Success omitted the responding model.");
        }
        desktop.ConnectionTestRequested = (_, _, _) => Task.FromException<ConnectionTestResult>(new OperationCanceledException());
        Click(); Wait(() => Text(1025).Contains("Connection Failed"));
        Check(Text(1025).Contains("timed out"), "Timeout was reported as success or cancellation.");
        desktop.ConnectionTestRequested = (_, _, _) => Task.FromException<ConnectionTestResult>(new InvalidOperationException("Sign in again to test this model."));
        Click(); Wait(() => Text(1025).Contains("Sign in again"));
        desktop.ConnectionTestRequested = (_, _, _) => Task.FromResult(new ConnectionTestResult("verified-exact-model", TimeSpan.FromSeconds(0.4)));
        Win32.SetWindowTextW(Item(1021), "8");
        desktop.SetStatus("Ready · Ctrl + Alt: dictate · Ctrl + Alt + Shift: ask AI");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0);
            Click(); Wait(() => Text(1025).Contains("Connected"));
            var tabs = Item(1014); var box = new Win32.Rect();
            Win32.SendMessageW(tabs, 0x130A, 5, (nint)(&box));
            var point = (nint)((uint)(ushort)(box.Left + 8) | ((uint)(ushort)(box.Top + 8) << 16));
            Win32.SendMessageW(tabs, 0x200, 0, point);
            Check(desktop.HoveredTab == 5 && Win32.SendMessageW(tabs, 0x130B, 0, 0) == 1, "Hover selected the tab or failed to track it.");
            if (output is not null) RenderHelp(window, output + (palette.Dark ? "-connection-dark.bmp" : "-connection-light.bmp"));
            Win32.SendMessageW(tabs, 0x2A3, 0, 0); Check(desktop.HoveredTab == -1, "Mouse leave retained hover paint.");
        }
        Provider(0); Check(Win32.IsWindowVisible(Item(112)) == 0, "Transcription-only mode offers an LLM test.");
        Win32.SetWindowTextW(Item(1021), "8");
        Provider(1);
        desktop.IsChatGptSignedIn = oldChat; desktop.IsClaudeSignedIn = oldClaude;
        desktop.HasGeminiApiKey = oldKey; desktop.IsLocalModelPresent = oldLocal;
        desktop.RefreshAuthentication();
        Console.WriteLine("PASS native tab hover and connection-test draft routing, cancellation, stale results, model/time, failures and both themes.");
    }
}
