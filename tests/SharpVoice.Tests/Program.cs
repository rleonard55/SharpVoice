using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;
using SharpVoice.Speech;
using SharpVoice.UI;

if (args is ["--idle-memory-app", var idleModelRoot, var memoryActivation]) { WindowsHelpSmoke.RunIdleMemoryApp(idleModelRoot, memoryActivation); return 0; }
if (args is ["--turnaround-whisper-benchmark", var turnaroundModel, var turnaroundWav]) { await TurnaroundChecks.Benchmark(turnaroundModel, turnaroundWav); return 0; }
if (args is ["--turnaround-config-check"]) { await TurnaroundChecks.Preferences(); return 0; }
if (args is ["--editor-hotkey-app", var editorHotkeyModels, var editorHotkeyGesture]) { WindowsHelpSmoke.RunEditorHotkeyApp(editorHotkeyModels, editorHotkeyGesture); return 0; }
if (args is ["--windows-automatic-monitor-smoke"]) { WindowsSettingsSmoke.RunAutomaticMonitoring(); return 0; }
if (args is ["--editor-hotkey-check"]) { await EditorChecks.HotkeyHandoff(); await RecordingGestureChecks.ResumedToggle(); return 0; }
if (args is ["--plugin-ask-check"]) { await AskAiChecks.CompletedPluginAnswers(); await AskAiChecks.StructuredQuestions(); await AskAiChecks.EditorContextRequests(); return 0; }
if (args is ["--followup-check"]) { await FollowUpChecks.Contracts(); await FollowUpChecks.CanceledDelivery(); await AskAiChecks.StructuredQuestions(); await SuccessCueChecks.QuestionAsset(); return 0; }
if (args is ["--answer-preferences-check"]) { await SmartAskChecks.AnswerPreferences(); return 0; }
if (args is ["--plugin-auto-update-check"]) { await EditorPluginChecks.Install(); await EditorPluginChecks.Failures(); await EditorPluginChecks.AutomaticUpdates(); await EditorPluginChecks.AutomaticUpdateSerialization(); await EditorPluginChecks.AutomaticUpdateStamp(); await EditorPluginChecks.AutomaticUpdateCancellation(); Console.WriteLine("PASS automatic editor plugin eligibility, safe installation, failure isolation, serialization, version stamps and cancellation."); return 0; }
if (args is ["--updates-check"]) { await ReleaseUpdateChecks.Preferences(); await ReleaseUpdateChecks.Policy(); await ReleaseUpdateChecks.Cancellation(); await ReleaseUpdateChecks.OneClickInstall(); return 0; }
if (args is ["--windows-provider-diagnostics-smoke", var providerOutput]) { WindowsHelpSmoke.Run(providerOutput, diagnosticsOnly: true); return 0; }
if (args is ["--provider-rejection-check"]) { await ProviderDiagnosticChecks.RejectionGuidance(); return 0; }
if (args is ["--provider-diagnostics-check"]) { await ProviderDiagnosticChecks.All(); return 0; }
if (args is ["--gemini-api-config-check"]) { await GeminiApiChecks.Configuration(); return 0; }
if (args is ["--wake-native", var wakeModel, var wakeWav, var wakeDictate, var wakeAsk]) { await WakeDetectorChecks.Native(wakeModel, wakeWav, wakeDictate, wakeAsk); return 0; }
if (args is ["--whisper-wake-clips", var vadPath, var selectedWhisper, var clipsDirectory]) { await WakeWhisperChecks.NativeClips(vadPath, selectedWhisper, clipsDirectory); return 0; }
if (args is ["--whisper-wake-regressions"])
{
    var regressionFailures = 0;
    foreach (var (name, check) in new (string, Func<Task>)[] { ("initial mode", WakeWhisperChecks.InitialMode), ("prefix windows", WakeWhisperChecks.PrefixWindows), ("long pause", WakeWhisperChecks.LongPause) })
        try { await check(); Console.WriteLine("PASS " + name); } catch (Exception ex) { regressionFailures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    return regressionFailures == 0 ? 0 : 1;
}
if (args is ["--wake-native", var wakeModel2, var wakeWav2, var wakeDictate2, var wakeAsk2, var expectedWake]) { await WakeDetectorChecks.Native(wakeModel2, wakeWav2, wakeDictate2, wakeAsk2, expectedWake); return 0; }
if (args is ["--wake-model-fixture", var wakeArchive, var wakeRoot]) { await WakeDetectorChecks.ModelFixture(wakeArchive, wakeRoot); return 0; }
if (args is ["--wake-token-check"]) { await WakeDetectorChecks.Tokenization(); return 0; }
if (args is ["--wake-diagnostics-ui", var diagnosticScreenshot]) { WindowsHelpSmoke.RunWakeDiagnosticsUi(diagnosticScreenshot); return 0; }
if (args is ["--wake-diagnostics-fixture", var diagnosticModel, var diagnosticDictate, var diagnosticAsk, var diagnosticOutput]) { await WindowsHelpSmoke.RunDiagnosticSession(diagnosticModel, diagnosticDictate, diagnosticAsk, diagnosticOutput); return 0; }
if (args is ["--wake-diagnostics-timeout", var timeoutModel, var timeoutWav, var timeoutOutput]) { await WindowsHelpSmoke.RunDiagnosticSession(timeoutModel, timeoutWav, timeoutWav, timeoutOutput, expectTimeout: true); return 0; }
if (args is ["--wake-epoch-check"]) { await WakeCaptureChecks.EpochAcrossDevices(); return 0; }
if (args is ["--wake-app-fixture", var appWakeRoot, var appDictateWav, var appAskWav, var appWakeOutput]) { WindowsHelpSmoke.RunWakeApp(appWakeRoot, appDictateWav, appAskWav, appWakeOutput); return 0; }
if (args is ["--wake-app-repeat", var repeatWakeRoot, var repeatDictateWav, var repeatAskWav, var repeatWakeOutput]) { WindowsHelpSmoke.RunWakeApp(repeatWakeRoot, repeatDictateWav, repeatAskWav, repeatWakeOutput, repeat: true); return 0; }
if (args is ["--wake-app-noise", var noiseWakeRoot, var noiseDictateWav, var noiseAskWav, var noiseWakeOutput]) { WindowsHelpSmoke.RunWakeApp(noiseWakeRoot, noiseDictateWav, noiseAskWav, noiseWakeOutput, repeat: true, noisy: true); return 0; }
if (args is ["--wake-app-long", var longWakeRoot, var longDictateWav, var longAskWav, var longWakeOutput]) { WindowsHelpSmoke.RunWakeApp(longWakeRoot, longDictateWav, longAskWav, longWakeOutput, repeat: true, noisy: true, cycles: 1); return 0; }

if (args is ["--connected-catalog-check"]) { await ConnectedChecks.LargeCatalog(); return 0; }
if (args is ["--connected-catalog-metadata-probe"])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await using var rpc = CodexRpc.Start();
    await using var gateway = await CodexGateway.OpenAsync(rpc, timeout.Token);
    Console.WriteLine("PASS metadata-only catalog discovery: " + gateway.Tools.Count + " tools. No tool was invoked."); return 0;
}
if (args is ["--connected-large-catalog-fixture"] or ["--connected-large-result-fixture"])
{
    var largeCatalog = args[0].Contains("catalog"); string? line;
    while ((line = await Console.In.ReadLineAsync()) is not null)
    {
        var request=ConnectedJson.Parse(line); if(!request.TryGetProperty("id",out var id))continue;
        var method=request.GetProperty("method").GetString();
        var result = method == "thread/start" ? "{\"thread\":{\"id\":\"fixture\"}}" : method == "mcpServerStatus/list" ? ConnectedJson.Build(w=>
        {
            w.WriteStartObject();w.WriteStartArray("data");w.WriteStartObject();w.WriteString("name","fixture");w.WriteString("runtimeStatus","connected");w.WriteStartObject("tools");
            for(var n=0;n<1200;n++){var name="read_"+n;w.WriteStartObject(name);w.WriteString("name",name);w.WriteString("description","Read fixture");w.WriteStartObject("annotations");w.WriteBoolean("readOnlyHint",n!=1198);w.WriteEndObject();w.WriteStartObject("inputSchema");w.WriteString("type","object");w.WriteString("description",new string('x',1000));if(n==0){for(var depth=0;depth<40;depth++){w.WriteStartObject("additionalProperties");w.WriteString("type","object");}for(var depth=0;depth<40;depth++)w.WriteEndObject();}w.WriteEndObject();w.WriteEndObject();}
            w.WriteEndObject();w.WriteEndObject();w.WriteEndArray();w.WriteNull("nextCursor");w.WriteEndObject();
        }) : largeCatalog ? "{}" : ConnectedJson.Build(w=>w.WriteStringValue(new string('x',1_100_000)));
        Console.WriteLine(ConnectedJson.Build(w=>{w.WriteStartObject();w.WritePropertyName("id");id.WriteTo(w);w.WritePropertyName("result");w.WriteRawValue(result,skipInputValidation:true);w.WriteEndObject();}));
    }
    return 0;
}
if (args is ["--connected-fake-mcp"]) { await ConnectedProtocol.Serve(); return 0; }
if (args is ["--connected-protocol-probe", var connectedExe, var connectedHome]) { await ConnectedProtocol.Probe(connectedExe, connectedHome); return 0; }
if (args is ["--windows-turnaround-smoke", var turnaroundOutput]) { WindowsHelpSmoke.Run(turnaroundOutput, turnaroundOnly:true); return 0; }
if (args is ["--windows-gemini-api-smoke", var googleApiOutput]) { WindowsHelpSmoke.Run(googleApiOutput, geminiOnly:true); return 0; }
if (args is ["--windows-connected-smoke", var connectedOutput]) { WindowsHelpSmoke.Run(connectedOutput, connectedOnly:true); return 0; }
if (args is ["--connected-hint-check"]) { await ConnectedChecks.Hints(); return 0; }
if (args is ["--connected-followup-check"]) { await ConnectedChecks.Loop(); return 0; }
if (args is ["--connected-render-check"]) { await ConnectedChecks.BoundedRendering(); return 0; }
if (args is ["--connected-review-check"]) { var errors=new List<string>();try { await ConnectedChecks.CompleteReview(); } catch(Exception ex){errors.Add(ex.Message);}try{await ConnectedChecks.Outcomes();}catch(Exception ex){errors.Add(ex.Message);}if(errors.Count>0)throw new Exception(string.Join("\r\n",errors));return 0; }
if (args is ["--connected-account-check"]) { await ConnectedChecks.Account(); return 0; }

if (args is ["--connected-fake-rpc"])
{
    var fake=new ConnectedFakeRpc();string? line;
    while((line=await Console.In.ReadLineAsync()) is not null)
    {
        var message=ConnectedChecks.Json(line);
        if(!message.TryGetProperty("method",out var method)||!message.TryGetProperty("id",out var id))continue;
        var p=message.GetProperty("params");
        if(method.GetString()=="initialize")
        {
            Console.WriteLine("{\"method\":\"item/commandExecution/requestApproval\",\"id\":999,\"params\":{}}");
            var denied=ConnectedChecks.Json((await Console.In.ReadLineAsync())!);
            if(!denied.TryGetProperty("error",out _))return 3;
        }
        if(p.TryGetProperty("arguments",out var a)&&a.TryGetProperty("hang",out _)){await Task.Delay(TimeSpan.FromMinutes(1));return 4;}
        var result=await fake.CallAsync(method.GetString()!,p,default);
        Console.WriteLine(ConnectedJson.Build(w=>{w.WriteStartObject();w.WritePropertyName("id");id.WriteTo(w);w.WritePropertyName("result");result.WriteTo(w);w.WriteEndObject();}));
    }
    return 0;
}

if (args is ["--oauth-page-previews", var oauthPreviewDirectory])
{
    Directory.CreateDirectory(oauthPreviewDirectory);
    foreach (var outcome in Enum.GetValues<OAuthCallbackOutcome>())
        File.WriteAllText(Path.Combine(oauthPreviewDirectory, outcome.ToString().ToLowerInvariant() + ".html"), OAuthCallbackPage.Render(outcome));
    Console.WriteLine("PASS exported embedded SharpVoice OAuth result pages.");
    return 0;
}

if (args is ["--gemini-live-check"])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var actual = "";
    var result = await new AntigravityClient().RefineAsync("Reply with exactly SHARPVOICE_OK", "Return only the requested answer.", "auto", _ => { }, model => actual = model, timeout.Token);
    if (result != "SHARPVOICE_OK") throw new Exception("Google did not return the exact fixed probe answer.");
    Console.WriteLine("PASS official Gemini fixed non-private request: " + actual);
    return 0;
}
if (args is ["--windows-google-snapshot", var googleSnapshot, var googleTheme])
{
    WindowsSettingsSnapshot.Run(googleSnapshot, googleTheme, 1, backend: "gemini_antigravity");
    return 0;
}
if (args is ["--windows-claude-snapshot", var claudeSnapshot, var claudeTheme])
{
    WindowsSettingsSnapshot.Run(claudeSnapshot, claudeTheme, 1, backend: "claude_code");
    return 0;
}

if (args is ["--claude-live-smoke"])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    using var http = new HttpClient();
    var refiner = new UnifiedRefiner(http, new RejectSessionProvider());
    string? actualModel = null;
    refiner.ModelResolved += model => actualModel = model;
    var answer = await refiner.RefineAsync("Reply with exactly these four words and nothing else: SharpVoice Claude connection OK", "notepad",
        new AppConfig { Backend = "claude_code" }, _ => { }, timeout.Token, askAi: true);
    if (answer != "SharpVoice Claude connection OK") throw new Exception("Claude did not return the expected fixed connection-test answer.");
    Console.WriteLine("PASS live Claude Code request through production refiner; model: " + actualModel);
    return 0;
}

if (args is ["--claude-status-smoke"])
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var state = await new ClaudeCodeClient().GetStatusAsync(timeout.Token);
    Console.WriteLine($"Claude Code installed: {state.Installed}; subscription signed in: {state.SignedIn}; {state.Message}");
    return 0;
}

if (args is ["--claude-process-fixture", var processMode])
{
    Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
    if (processMode == "echo") { Console.Write(await Console.In.ReadToEndAsync()); Console.Error.Write(new string('e', 100_000)); return 0; }
    if (processMode == "large") { Console.Write(new string('x', 2_000_001)); Console.Out.Flush(); }
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

if (args is ["--windows-meter-snapshot", var meterOutput, var meterStyle, var meterLevel, var meterGate])
{
    WindowsSettingsSnapshot.Run(meterOutput, meterStyle, 0, float.Parse(meterLevel, System.Globalization.CultureInfo.InvariantCulture), float.Parse(meterGate, System.Globalization.CultureInfo.InvariantCulture));
    return 0;
}

if (args.Length > 0 && args[0] == "--file")
{
    var input = await Console.In.ReadToEndAsync();
    if (args.Any(x => x.Contains("Private fixture text"))) return 5;
    var voice = args[Array.IndexOf(args, "--voice") + 1];
    if (voice == "hang") { await Task.Delay(TimeSpan.FromSeconds(30)); return 0; }
    var output = Console.OpenStandardOutput();
    if (voice == "oversize") { await output.WriteAsync(new byte[8_000_001]); await output.FlushAsync(); return 0; }
    if (voice != "fixture" || input != "Private fixture text") return 4;
    await output.WriteAsync(new byte[64]); await output.FlushAsync(); return 0;
}
if (args is ["--windows-plugin-install-smoke", var pluginEditorExe, var pluginProfile]) { await EditorPluginChecks.RealInstall(pluginEditorExe, pluginProfile); return 0; }
if (args is ["--windows-editor-extension-smoke", var editorExe, var extensionRoot, var editorReport]) { WindowsEditorExtensionSmoke.Run(editorExe, extensionRoot, editorReport); return 0; }
if (args is ["--windows-visual-menus", var menuOutput]) { WindowsHelpSmoke.CaptureMenus(menuOutput); return 0; }
if (args is ["--windows-hud-snapshot", var hudOutput]) { WindowsHudSmoke.Run(hudOutput); return 0; }

if (args is ["--windows-selection-smoke"]) { WindowsSelectionSmoke.Run(); return 0; }

if (args is ["--windows-cue-latency-smoke"]) { await WindowsCueLatencySmoke.Run(); return 0; }

if (args is ["--llama-converter-smoke"])
{
    LlamaConverterChecks.Metadata();
    LlamaConverterChecks.TensorSplits();
    Console.WriteLine("PASS model metadata and tensor-split converters preserve JSON options with reflection disabled.");
    return 0;
}

if (args is ["--windows-native-bundle-smoke"])
{
    var root = NativeRuntimeBundle.Ensure() ?? throw new Exception("No bundled native runtime.");
    var required = new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" };
    using var process = System.Diagnostics.Process.GetCurrentProcess();
    foreach (var name in required)
    {
        var expected = Path.Combine(root, "cpp", name);
        if (!File.Exists(expected)) throw new Exception("Missing bundled C++ runtime: " + name);
        if (!process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(x => string.Equals(x.FileName, expected, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("C++ dependency was not loaded from its bundled path: " + name);
    }
    Console.WriteLine("PASS all four Microsoft C++/OpenMP dependencies loaded explicitly from the verified bundle cache.");
    return 0;
}

if (args is ["--windows-performance-smoke"]) { await WindowsPerformanceSmoke.Run(); return 0; }

if (args is ["--embedded-smoke"])
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await new EmbeddedRefiner(http).RefineAsync("hello world this is a test", DefaultPrompts.MasterPrompt, _ => { }, timeout.Token);
    if (!result.Contains("hello", StringComparison.OrdinalIgnoreCase) || result.Length > 300) throw new Exception("Offline model did not return a short refinement of the fixture.");
    Console.WriteLine($"PASS offline Qwen fixture with full master prompt ({watch.Elapsed.TotalSeconds:F2}s): {result}");
    return 0;
}

if (args is ["--windows-updates-smoke"]) { WindowsHelpSmoke.Run(updatesOnly: true); return 0; }
if (args is ["--windows-help-smoke"]) { WindowsHelpSmoke.Run(); return 0; }
if (args is ["--windows-help-smoke", var helpOutput]) { WindowsHelpSmoke.Run(helpOutput); return 0; }

if (args is ["--windows-hud-smoke"]) { WindowsHudSmoke.Run(); return 0; }
if (args is ["--windows-hud-window-smoke"]) { WindowsHudWindowChecks.Run(); return 0; }
if (args is ["--windows-onboarding-smoke"]) { WindowsOnboardingChecks.Run(); return 0; }
if (args is ["--windows-onboarding-smoke", var setupOutput]) { WindowsOnboardingChecks.Run(setupOutput); return 0; }
if (args is ["--windows-onboarding-app-smoke", var setupModel, var setupWav]) { WindowsOnboardingChecks.RunApp(setupModel, setupWav); return 0; }

if (args is ["--config-smoke", var configPath])
{
    ConfigManager.Load(configPath).Validate();
    Console.WriteLine("PASS saved settings load and validate without changing preferences.");
    return 0;
}

if (args is ["--windows-settings-snapshot", var pageSnapshotPath, var pageTheme, var pageNumber])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    WindowsSettingsSnapshot.Run(pageSnapshotPath, pageTheme, int.Parse(pageNumber));
    return 0;
}

if (args is ["--windows-settings-snapshot", var themedSnapshotPath, var snapshotTheme])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    WindowsSettingsSnapshot.Run(themedSnapshotPath, snapshotTheme);
    return 0;
}

if (args is ["--windows-settings-snapshot", var snapshotPath])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    WindowsSettingsSnapshot.Run(snapshotPath);
    return 0;
}

if (args is ["--windows-device-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var devices = AudioDeviceCatalog.Enumerate();
    if (devices.Count < 2 || devices[0].Id != "" || devices.Skip(1).Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name)))
        throw new Exception("Expected the default choice and at least one named recording endpoint.");
    using var recorder = new WasapiAudioRecorder(-65, 2, devices[1].Id);
    if (recorder.IsAvailable) throw new Exception("An unstarted recorder must not report ready.");
    await recorder.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
    if (!recorder.IsAvailable) throw new Exception("Started microphone is not available.");
    recorder.Begin();
    await Task.Delay(150);
    using var audio = await recorder.EndAsync(default);
    if (audio.Samples.Length < 800) throw new Exception("Selected microphone did not deliver audio frames.");
    recorder.Dispose();
    if (recorder.IsAvailable) throw new Exception("Disposed microphone still reports available.");
    using var unavailable = new WasapiAudioRecorder(-65, 2, "sharpvoice-test-nonexistent-endpoint");
    try { await unavailable.StartAsync().WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Unavailable microphone unexpectedly started."); }
    catch (Exception ex) when (ex is COMException or ArgumentException) { }
    if (unavailable.IsAvailable) throw new Exception("Failed microphone reports available.");
    Console.WriteLine($"PASS {devices.Count - 1} named microphone endpoints enumerated and explicit-device capture completed; no audio saved.");
    return 0;
}

if (args is ["--windows-usability-smoke", var usabilityScenario]) { WindowsHelpSmoke.RunUsability(usabilityScenario); return 0; }
if (args is ["--windows-compact-audio-smoke"]) { WindowsHelpSmoke.RunCompactAudio(); return 0; }
if (args is ["--windows-settings-smoke", var preset])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    WindowsSettingsSmoke.Run(custom: preset == "custom");
    return 0;
}

if (args is ["--windows-room-noise"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    using var recorder = new WasapiAudioRecorder(-90, 10);
    await recorder.StartAsync();
    await Task.Delay(500);
    var levels = new List<float>();
    for (var i = 0; i < 100; i++)
    {
        await Task.Delay(50);
        levels.Add(recorder.LevelDb);
    }
    levels.Sort();
    Console.WriteLine($"Room sample (5 seconds, default microphone, no audio saved): median {levels[50]:F1} dBFS; 95th percentile {levels[94]:F1} dBFS; peak {levels[^1]:F1} dBFS.");
    return 0;
}

if (args is ["--windows-feedback-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    using var desktop = new NativeDesktop(new AppConfig());
    desktop.Initialize(showSettings: false);
    var neutral = Win32.SendMessageW(desktop.SettingsHandle, 0x7F, 1, 0); // WM_GETICON
    if (neutral == 0) throw new Exception("No microphone icon installed.");
    desktop.SetRecording(true);
    var recording = Win32.SendMessageW(desktop.SettingsHandle, 0x7F, 1, 0);
    if (recording == 0 || recording == neutral) throw new Exception("Recording did not change the microphone icon.");
    desktop.SetRecording(false);
    if (Win32.SendMessageW(desktop.SettingsHandle, 0x7F, 1, 0) != neutral) throw new Exception("Processing did not return to neutral.");
    desktop.SetStatus("No text transcribed.", isError: true, warningSound: true);
    var warning = Win32.SendMessageW(desktop.SettingsHandle, 0x7F, 1, 0);
    if (warning == 0 || warning == neutral || warning == recording) throw new Exception("Warning did not use a distinct microphone icon.");
    desktop.SetActive(false);
    if (Win32.SendMessageW(desktop.SettingsHandle, 0x7F, 1, 0) != warning) throw new Exception("HUD cleanup cleared the warning icon.");
    var finish = Task.Run(async () => { await Task.Delay(800); desktop.Post(() => Win32.PostQuitMessage(0)); });
    desktop.Run();
    await finish;
    Console.WriteLine("PASS native microphone icon transitions, HUD/error state and queued start/stop/warning cue playback.");
    return 0;
}

if (args is ["--windows-sound-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    foreach (var asset in new[] { "recording-start", "recording-stop", "warning" })
    {
        using var source = typeof(NativeDesktop).Assembly.GetManifestResourceStream($"SharpVoice.UI.Assets.{asset}.wav") ?? throw new Exception("Missing sound asset.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var data = buffer.ToArray();
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { if (Win32.PlaySound(pinned.AddrOfPinnedObject(), 0, 0x6) == 0) throw new Exception("Windows rejected the " + asset + " cue."); }
        finally { pinned.Free(); }
    }
    Console.WriteLine("PASS Windows accepted all three embedded PCM sound cues.");
    return 0;
}

if (args is ["--windows-injection-smoke"])
{
    WindowsInjectionSmoke.Run();
    return 0;
}
if (args is ["--windows-richedit-smoke"])
{
    WindowsInjectionSmoke.Run(richEdit: true);
    return 0;
}
if (args is ["--windows-clipboard-smoke"])
{
    WindowsInjectionSmoke.Run(richEdit: true, method: "clipboard");
    return 0;
}

if (args is ["--chatgpt-live-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The saved session requires Windows DPAPI.");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("SharpVoice/0.1.0");
    using var auth = new ChatGptAuthManager(http);
    auth.Initialize();
    var config = ConfigManager.Load() with { Backend = "chatgpt_oauth", Mode = "prose", CustomPrompt = "", MinimumAiWords = 0 };
    var result = await new UnifiedRefiner(http, auth).RefineAsync("hello world this is a test", "notepad", config, _ => { }, default);
    if (string.IsNullOrWhiteSpace(result)) throw new Exception("No completed refinement returned.");
    Console.WriteLine("PASS live ChatGPT model selection and completed refinement using SharpVoice's saved session and a fixed test transcript.");
    return 0;
}

if (args is ["--windows-audio-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This smoke test requires Windows.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var recorder = new WasapiAudioRecorder(-42, 2);
    await recorder.StartAsync().WaitAsync(timeout.Token);
    recorder.Begin();
    await Task.Delay(150, timeout.Token);
    using var audio = await recorder.EndAsync(timeout.Token);
    if (audio.Samples.Length < 800) throw new Exception("WASAPI did not deliver the expected audio frames.");
    recorder.Configure(-42, true);
    await Task.Delay(100, timeout.Token);
    recorder.Configure(-42, false);
    await Task.Delay(100, timeout.Token);
    recorder.Begin();
    await Task.Delay(100, timeout.Token);
    using var resumed = await recorder.EndAsync(timeout.Token);
    if (resumed.Samples.Length < 800) throw new Exception("WASAPI did not resume capture.");
    Console.WriteLine("PASS Windows WASAPI startup, recording, post-roll, pause/resume and cleanup. No audio was saved.");
    return 0;
}

if (args is ["--windows-native-smoke"])
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This smoke test requires Windows.");
    using var desktop = new NativeDesktop(new AppConfig());
    desktop.Initialize(showSettings: false);
    desktop.Post(() => Win32.PostQuitMessage(0));
    desktop.Run();
    Console.WriteLine("PASS Windows native tray, settings/HUD window creation, global hook, message pump and cleanup.");
    return 0;
}

if (args is ["--live-whisper-benchmark", var liveModel, var liveWav]) { await LiveTranscriptionChecks.Benchmark(liveModel, liveWav); return 0; }

if (args is ["--whisper-smoke", var modelPath, var wavPath])
{
    if (!await ModelManager.VerifyAsync(modelPath, ModelManager.HashFor(Path.GetFileNameWithoutExtension(modelPath).Replace("ggml-", "")), default)) throw new Exception("Smoke-test model hash mismatch.");
    using var file = File.OpenRead(wavPath);
    using var reader = new BinaryReader(file);
    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new Exception("Expected RIFF WAV fixture.");
    reader.ReadUInt32();
    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new Exception("Expected WAVE fixture.");
    float[]? samples = null;
    bool validFormat = false;
    while (file.Position + 8 <= file.Length)
    {
        var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
        var size = reader.ReadUInt32();
        var next = file.Position + size + (size & 1);
        if (chunk == "fmt ")
        {
            var pcm = reader.ReadUInt16(); var channels = reader.ReadUInt16(); var rate = reader.ReadUInt32();
            reader.ReadUInt32(); reader.ReadUInt16(); var bits = reader.ReadUInt16();
            validFormat = pcm == 1 && channels == 1 && rate == 16000 && bits == 16;
        }
        else if (chunk == "data")
        {
            if (!validFormat) throw new Exception("Fixture must be mono PCM16 at 16 kHz.");
            samples = new float[size / 2];
            for (var i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
        }
        file.Position = next;
    }
    if (samples is null) throw new Exception("WAV has no audio data.");
    using var engine = new WhisperEngine();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    await engine.PreloadAsync(modelPath, timeout.Token);
    Console.WriteLine($"MODEL_LOAD_MS={watch.Elapsed.TotalMilliseconds:F2}");
    watch.Restart();
    var result = await engine.TranscribeAsync(modelPath, samples, "en", _ => { }, timeout.Token);
    Console.WriteLine($"INFERENCE_MS={watch.Elapsed.TotalMilliseconds:F2}");
    var process = System.Diagnostics.Process.GetCurrentProcess();
    process.Refresh();
    Console.WriteLine($"BEFORE_TRIM_MIB={process.WorkingSet64 / 1048576d:F2}");
    if (!MemoryTrimmer.Trim()) throw new Exception("Working-set trim failed.");
    await Task.Delay(1000);
    process.Refresh();
    Console.WriteLine($"AFTER_TRIM_MIB={process.WorkingSet64 / 1048576d:F2}");
    watch.Restart();
    var second = await engine.TranscribeAsync(modelPath, samples, "en", _ => { }, timeout.Token);
    Console.WriteLine($"AFTER_TRIM_INFERENCE_MS={watch.Elapsed.TotalMilliseconds:F2}");
    if (!second.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception("Trimmed model could not transcribe again.");
    if (!result.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception("Real Whisper fixture did not produce the expected speech.");
    Console.WriteLine("PASS real WhisperEngine inference with verified tiny.en and upstream JFK audio fixture.");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Plugin Ask displays completed plain/fenced answers while rejecting malformed or interrupted output", AskAiChecks.CompletedPluginAnswers),
    ("Indigo light/dark palettes keep readable text and semantic status contrast", ThemeChecks.IndigoContrast),
    ("Provider diagnostics explain TLS without leaking exception text", ProviderDiagnosticChecks.TlsExplanation),
    ("Provider diagnostics retain only bounded allowlisted fields", ProviderDiagnosticChecks.JournalPrivacy),
    ("Provider diagnostics classify network errors without duplicate records", ProviderDiagnosticChecks.Categories),
    ("HTML responses are distinguished from truncated SSE", ProviderDiagnosticChecks.ResponseMeaning),
    ("Provider rejections retain fixed actionable guidance", ProviderDiagnosticChecks.RejectionGuidance),
    ("Nested deadlines preserve the active provider operation", ProviderDiagnosticChecks.NestedDeadline),
    ("Token refresh failures have sanitized diagnostics", OAuthCallbackChecks.Diagnostics),
    ("Provider deadlines differ from caller cancellation", ProviderDiagnosticChecks.Lifetimes),
    ("ChatGPT catalog and inference failures have safe stage attribution", ProviderDiagnosticChecks.ChatGptIntegration),
    ("Gemini HTTP and response-body failures have safe diagnostics", ProviderDiagnosticChecks.GeminiIntegration),
    ("Whisper wake matches only complete prefixes and prioritizes dictation", WakeWhisperChecks.Prefix),
    ("Whisper wake performs no inference during silence and activates once", WakeWhisperChecks.Silence),
    ("Whisper wake tracks speech ending independently and cancels background inference", WakeWhisperChecks.SlowInference),
    ("Whisper wake discards stale results and suspends inference", WakeWhisperChecks.Stale),
    ("Whisper wake waits for the longer phrase before activating a shared prefix", WakeWhisperChecks.SharedPrefix),
    ("Whisper wake retains a longer initially detected mode", WakeWhisperChecks.InitialMode),
    ("Whisper wake analyzes deterministic early prefix windows", WakeWhisperChecks.PrefixWindows),
    ("Whisper wake respects the configured pause during slow inference", WakeWhisperChecks.LongPause),
    ("Wake diagnostic capture reports gaps and liveness only when enabled", WakeDiagnosticChecks.CaptureCounters),
    ("Wake diagnostic early Stop cancels preparation before capture", WakeDiagnosticChecks.EarlyStop),
    ("Wake diagnostic shutdown releases resources after worker faults", WakeDiagnosticChecks.FaultedShutdown),
    ("Wake diagnostic guided trials distinguish prefixes, missed phrases and false activations", WakeDiagnosticChecks.GuidedModes),
    ("Wake diagnostic main-app guard blocks capture while retaining export", WakeDiagnosticChecks.MainAppGuard),
    ("Wake diagnostic opt-in recordings have bounded WAV length and count", WakeDiagnosticChecks.AudioBounds),
    ("Wake diagnostic exports are bounded and exclude adjacent private files and default audio", WakeDiagnosticChecks.JournalPrivacy),
    ("Wake phrases use model-compatible SentencePiece tokens", WakeDetectorChecks.Tokenization),
    ("Corrupt and canceled wake downloads never become ready", WakeDetectorChecks.Assets),
    ("Wake preferences migrate safely and preserve explicit choices", WakePhraseChecks.Configuration),
    ("Overlapping wake phrases choose the longest prefix without losing request text", WakePhraseChecks.Prefixes),
    ("Wake lifecycle suspends for playback, pause, busy state and rearm", WakePhraseChecks.Lifecycle),
    ("Wake capture shares one microphone and preserves continuous audio", WakeCaptureChecks.DemandAndHandoff),
    ("Wake capture rejects expired audio and leaves noise gating intact", WakeCaptureChecks.BoundsAndGating),
    ("Replacement microphone epochs cannot accept old detector callbacks", WakeCaptureChecks.EpochAcrossDevices),
    ("Wake capture rejects device discontinuities", WakeCaptureChecks.Discontinuity),
    ("Resampled endpoint clocks preserve continuous wake audio", WakeCaptureChecks.ResampledContinuity),
    ("Wake capture retains seeded silence after confirmation", WakeCaptureChecks.SeededSilence),
    ("Wake stop bypasses toggle latch and preserves nested promotion", RecordingGestureChecks.WakeRouting),
    ("Keep Ready startup waits for native readiness and propagates failures", MicrophoneActivationChecks.KeepReadyStartup),
    ("On Hotkey opens only for dictation and closes after tail or cancellation", MicrophoneActivationChecks.OnHotkey),
    ("Explicit preview is temporary and Keep Ready preserves local pre-roll", MicrophoneActivationChecks.PreviewAndKeepReady),
    ("Cancelled or failed microphone startup cannot report recording readiness", MicrophoneActivationChecks.StartupCancellationAndFailure),
    ("Microphone activation defaults to On Hotkey and persists safely", MicrophoneActivationChecks.Configuration),
    ("Llama metadata JSON converters round-trip with reflection disabled", () => { LlamaConverterChecks.Metadata(); return Task.CompletedTask; }),
    ("Concurrent pre-roll snapshots contain contiguous samples through wraps", async () =>
    {
        using var ring = new AudioBufferRing(257);
        var writer = Task.Run(() =>
        {
            for (var block = 0; block < 10000; block++)
            {
                float[] values = Enumerable.Range(block * 7, 7).Select(x => (float)x).ToArray();
                ring.Write(values);
            }
        });
        var snapshots = 0;
        do
        {
            var values = ring.Snapshot();
            for (var i = 1; i < values.Length; i++) Equal(values[i - 1] + 1, values[i]);
            snapshots++;
        } while (!writer.IsCompleted || snapshots < 100);
        await writer;
        Equal(69999f, ring.Snapshot()[^1]);
    }),
    ("All standard and quantized model selections validate and have pinned hashes", () =>
    {
        Equal(6, SpeechModels.All.Length);
        foreach (var model in SpeechModels.All)
        {
            (new AppConfig { SpeechModel = model }).Validate();
            Equal(32, Convert.FromHexString(ModelManager.HashFor(model)).Length);
        }
        Equal(6, SpeechModels.All.Select(ModelManager.HashFor).Distinct().Count());
        return Task.CompletedTask;
    }),
    ("Pooled audio owns only the recorded slice and clears on return", () =>
    {
        var pool = new TrackingFloatPool();
        var buffer = pool.Rent(1600);
        buffer.AsSpan().Fill(0.25f);
        var audio = new AudioRecording(buffer, 800, true, pool);
        Equal(800, audio.Samples.Length);
        Equal(true, audio.HasSpeech);
        audio.Dispose(); audio.Dispose();
        Equal(1, pool.Returns);
        Equal(true, pool.Cleared);
        Throws<ObjectDisposedException>(() => _ = audio.Samples);
        return Task.CompletedTask;
    }),

    ("Dual dictation and Ask AI shortcuts route one gesture safely", () => { DualHotkeyChecks.Run(); return Task.CompletedTask; }),
    ("Claude Code routes private request context and reports the actual model", ClaudeCodeChecks.Routing),
    ("Claude Code rejects incomplete results and unsupported authentication", ClaudeCodeChecks.Failures),
    ("Claude child process preserves text, bounds output and cancels", ClaudeCodeChecks.Processes),
    ("Ask AI answers requests without dictation cleanup or envelopes", AskAiChecks.Requests),
    ("Ask AI treats selected text as optional context", AskAiChecks.Context),
    ("Ask AI guards blank speech, missing AI and oversized context", AskAiChecks.Guards),
    ("Selected source ownership survives post-roll cancellation and late capture", SelectionOwnershipChecks.Run),
    ("Question cue is distinct two-note PCM", SuccessCueChecks.QuestionAsset),
    ("Plugin Ask uses structured questions without changing native answers", AskAiChecks.StructuredQuestions),
    ("Plugin Ask sends file identity and selected code to the provider", AskAiChecks.EditorContextRequests),
    ("Canceled follow-up delivery cannot publish or cue a question", FollowUpChecks.CanceledDelivery),
    ("Editor project sources and guidance remain bounded", EditorChecks.ProjectContext),
    ("Follow-up results and reply choices are bounded", FollowUpChecks.Contracts),
    ("Insertion success cue is short, subdued and distinct PCM", SuccessCueChecks.Asset),
    ("Economical catalog ranking and safe metadata", ModelEditingChecks.Catalog),
    ("Selected editing payload and account-bound model resolution", ModelEditingChecks.EditingAndResolvedModel),
    ("Selected editing and empty speech guards", ModelEditingChecks.Guards),
    ("Cue construction retries ordinary failures but stops when a driver retains resources", () =>
    {
        var blocked = false; var attempts = 0;
        object FailRetained() { attempts++; throw new CueResourcesRetainedException(new IOException("Driver failure")); }
        Equal<object?>(null, CueOutputRecovery.TryOpen(FailRetained, ref blocked));
        Equal(true, blocked);
        Equal<object?>(null, CueOutputRecovery.TryOpen(FailRetained, ref blocked));
        Equal(1, attempts);
        blocked = false; attempts = 0;
        object FailOrdinary() { attempts++; throw new IOException("Output disconnected"); }
        Equal<object?>(null, CueOutputRecovery.TryOpen(FailOrdinary, ref blocked));
        Equal<object?>(null, CueOutputRecovery.TryOpen(FailOrdinary, ref blocked));
        Equal(false, blocked); Equal(2, attempts);
        var recovered = new object();
        Equal(recovered, CueOutputRecovery.TryOpen(() => recovered, ref blocked));
        return Task.CompletedTask;
    }),
    ("Cue cleanup retains driver-owned buffers and callback handles on failure", () =>
    {
        var released = new List<nint>();
        var resetCalls = 0; var closeCalls = 0; var callbackReleases = 0;
        var result = WaveOutCleanup.TryRelease([1, 2], () => resetCalls++, b => b == 1, released.Add,
            () => { closeCalls++; return true; }, () => callbackReleases++);
        Equal(false, result);
        Equal(true, released.SequenceEqual([(nint)1]));
        Equal(1, resetCalls); Equal(0, closeCalls); Equal(0, callbackReleases);
        released.Clear();
        result = WaveOutCleanup.TryRelease([1], () => { }, _ => true, released.Add,
            () => false, () => callbackReleases++);
        Equal(false, result); Equal(1, released.Count); Equal(0, callbackReleases);
        result = WaveOutCleanup.TryRelease([1], () => { }, _ => true, _ => { },
            () => true, () => callbackReleases++);
        Equal(true, result); Equal(1, callbackReleases);
        return Task.CompletedTask;
    }),
    ("Recording feedback plays distinct start/stop cues once and returns to neutral for processing", () =>
    {
        var feedback = new RecordingFeedback();
        Equal(MicrophoneState.Neutral, feedback.State);
        Equal(FeedbackCue.RecordingStart, feedback.BeginRecording());
        Equal(MicrophoneState.Recording, feedback.State);
        Equal(FeedbackCue.None, feedback.BeginRecording());
        Equal(FeedbackCue.RecordingStop, feedback.EndRecording());
        Equal(MicrophoneState.Neutral, feedback.State);
        Equal(FeedbackCue.None, feedback.EndRecording());
        return Task.CompletedTask;
    }),
    ("Warning feedback survives recording cleanup and clears on the next attempt", () =>
    {
        var feedback = new RecordingFeedback();
        feedback.BeginRecording();
        Equal(FeedbackCue.Warning, feedback.ReportError());
        feedback.EndRecording();
        Equal(MicrophoneState.Error, feedback.State);
        feedback.BeginRecording();
        Equal(MicrophoneState.Recording, feedback.State);
        feedback.EndRecording();
        feedback.ReportError();
        feedback.ClearError();
        Equal(MicrophoneState.Neutral, feedback.State);
        return Task.CompletedTask;
    }),
    ("Empty transcripts never authenticate, discover models, refine or preview", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => throw new Exception("Empty transcription must not make a network request.")));
        var refiner = new UnifiedRefiner(http, new RejectSessionProvider());
        foreach (var backend in new[] { "chatgpt_oauth", "none", "embedded" })
        foreach (var transcript in new[] { "", " ", "\r\n\t", "\u2003" })
        {
            var result = await refiner.RefineAsync(transcript, "notepad", new AppConfig { Backend = backend }, _ => throw new Exception("Empty transcription must not be previewed."), default);
            Equal("", result);
        }
    }),
    ("Refinement uses the supplied master prompt verbatim before optional instructions", () =>
    {
        var master = DefaultPrompts.MasterPrompt;
        Equal("D5AA064E3E95928651C6D0BF045657F72B0373A0CCC7F2AE8E7B6BAFC41C258C", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(master))));
        Equal(master, PromptTemplates.For("prose", ""));
        Equal(true, PromptTemplates.For("code", "Preserve our project terms.").StartsWith(master, StringComparison.Ordinal));
        Equal(true, PromptTemplates.For("code", "Preserve our project terms.").EndsWith("Preserve our project terms.", StringComparison.Ordinal));
        return Task.CompletedTask;
    }),
    ("Pre-filled general instructions do not duplicate cleanup rules or override selection editing", () =>
    {
        var master = DefaultPrompts.MasterPrompt;
        Equal(master, PromptTemplates.For("prose", master));
        var config = new AppConfig { CustomPrompt = master.Replace("\r\n", "\n") + "\n\nPreserve ticket IDs." };
        var cleanup = PromptTemplates.For("prose", config);
        Equal(cleanup.IndexOf("# Intelligent Dictation Cleanup Engine", StringComparison.Ordinal), cleanup.LastIndexOf("# Intelligent Dictation Cleanup Engine", StringComparison.Ordinal));
        Equal(true, cleanup.Contains("Preserve ticket IDs."));
        var selection = PromptTemplates.ForSelection("prose", config);
        Equal(false, selection.Contains("# Intelligent Dictation Cleanup Engine"));
        Equal(true, selection.Contains("Preserve ticket IDs."));
        return Task.CompletedTask;
    }),
    ("Editing the full cleanup template stays isolated from selection editing and preserves profile preferences", () =>
    {
        var template = "Use British English.\n\n" + DefaultPrompts.MasterPrompt.Replace("BALANCED (default)", "LIGHT (default)");
        var config = new AppConfig { CleanupPrompt = template, CustomPrompt = "Preserve ticket IDs.", Profiles = [new("Mail", "outlook", Instructions: "Keep paragraphs short.")] };
        var resolved = WritingTools.Resolve(config, "outlook");
        var cleanup = PromptTemplates.For("prose", resolved);
        Equal(true, cleanup.StartsWith(template, StringComparison.Ordinal));
        Equal(cleanup.IndexOf("# Intelligent Dictation Cleanup Engine", StringComparison.Ordinal), cleanup.LastIndexOf("# Intelligent Dictation Cleanup Engine", StringComparison.Ordinal));
        Equal(true, cleanup.Contains("Preserve ticket IDs."));
        Equal(true, cleanup.Contains("Keep paragraphs short."));
        var selection = PromptTemplates.ForSelection("prose", resolved);
        Equal(false, selection.Contains("# Intelligent Dictation Cleanup Engine"));
        Equal(false, selection.Contains("Use British English."));
        Equal(true, selection.Contains("Preserve ticket IDs."));
        Equal(true, selection.Contains("Keep paragraphs short."));
        Equal(DefaultPrompts.MasterPrompt, PromptTemplates.For("prose", new AppConfig { CleanupPrompt = "" }));
        return Task.CompletedTask;
    }),
    ("Ctrl+Alt starts once in either order and stops on either release", () =>
    {
        foreach (var order in new[] { new[] { 0xA2, 0xA4 }, new[] { 0xA5, 0xA3 } })
        {
            foreach (var release in order)
            {
                var keys = new HotkeyStateMachine(0);
                Equal(HotkeyTransition.None, keys.Process(order[0], true).Transition);
                Equal(HotkeyTransition.Pressed, keys.Process(order[1], true).Transition);
                Equal(HotkeyTransition.None, keys.Process(order[1], true).Transition);
                Equal(HotkeyTransition.Released, keys.Process(release, false).Transition);
                Equal(HotkeyTransition.None, keys.Process(order.Single(k => k != release), false).Transition);
            }
        }
        return Task.CompletedTask;
    }),
    ("Ctrl+Alt tracks both sides and ignores injected modifiers", () =>
    {
        var keys = new HotkeyStateMachine(0);
        keys.Process(0xA2, true);
        keys.Process(0xA3, true);
        Equal(HotkeyTransition.Pressed, keys.Process(0xA4, true).Transition);
        Equal(HotkeyTransition.None, keys.Process(0xA2, false).Transition);
        Equal(HotkeyTransition.None, keys.Process(0xA3, false, injected: true).Transition);
        Equal(HotkeyTransition.Released, keys.Process(0xA4, false).Transition);
        var synthetic = new HotkeyStateMachine(0);
        synthetic.Process(0xA2, true, injected: true);
        Equal(HotkeyTransition.None, synthetic.Process(0xA5, true).Transition);
        return Task.CompletedTask;
    }),
    ("Modifier hotkey preserves normal key events; function-key mode still suppresses its key", () =>
    {
        var modifiers = new HotkeyStateMachine(0);
        Equal(false, modifiers.Process(0xA2, true).Suppress);
        Equal(false, modifiers.Process(0xA4, true).Suppress);
        Equal(false, modifiers.Process(0xA2, false).Suppress);
        var function = new HotkeyStateMachine(0x77);
        Equal(HotkeyTransition.None, function.Process(0x77, true, injected: true).Transition);
        Equal(HotkeyTransition.Pressed, function.Process(0x77, true).Transition);
        Equal(true, function.Process(0x77, true).Suppress);
        Equal(HotkeyTransition.Released, function.Process(0x77, false).Transition);
        return Task.CompletedTask;
    }),
    ("PKCE matches RFC 7636 S256 test vector", () =>
    {
        Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        return Task.CompletedTask;
    }),
    ("OAuth result pages have branded safe HTML and correct response framing", OAuthCallbackChecks.Pages),
    ("OAuth failure, cancellation and invalid callbacks never claim success", OAuthCallbackChecks.Failures),
    ("A disconnected browser cannot invalidate a saved OAuth login", OAuthCallbackChecks.DisconnectedBrowser),
    ("OAuth reconnect preserves the old session until replacement is saved", OAuthCallbackChecks.Reconnect),
    ("OAuth browser success waits for exchanged and saved credentials", OAuthCallbackChecks.CompletionOrder),
    ("OAuth callback decodes values without confusing embedded equals", () =>
    {
        var query = ChatGptAuthManager.ParseQuery("?code=abc%3Ddef&state=a%2Bb&error_description=access+denied");
        Equal("abc=def", query["code"]); Equal("a+b", query["state"]); Equal("access denied", query["error_description"]);
        return Task.CompletedTask;
    }),
    ("Pre-roll preserves chronological order after several wraps", () =>
    {
        using var ring = new AudioBufferRing(4);
        ring.Write([1, 2]); Equal("1,2", string.Join(',', ring.Snapshot()));
        ring.Write([3, 4, 5, 6, 7]); Equal("4,5,6,7", string.Join(',', ring.Snapshot()));
        ring.Clear(); Equal(0, ring.Snapshot().Length);
        return Task.CompletedTask;
    }),
    ("Noise gate silences low energy and passes a spoken-range signal", () =>
    {
        var filter = new AudioFilter();
        var quiet = Enumerable.Range(0, 1600).Select(i => 0.00001f * MathF.Sin(i * 0.1f)).ToArray();
        filter.Process(quiet, -42); Equal(true, quiet.All(x => x == 0));
        var loud = Enumerable.Range(0, 1600).Select(i => 0.3f * MathF.Sin(i * 0.1f)).ToArray();
        Equal(true, filter.Process(loud, -42) > -42); Equal(true, loud.Any(x => x != 0));
        return Task.CompletedTask;
    }),
    ("Default gate preserves quiet speech while rejecting digital silence", () =>
    {
        var quiet = Enumerable.Range(0, 1600).Select(i => 0.002f * MathF.Sin(i * 0.1f)).ToArray();
        var db = new AudioFilter().Process(quiet, new AppConfig().NoiseGateDb);
        Equal(true, db < -42);
        Equal(true, quiet.Any(x => x != 0));
        var silence = new float[1600];
        new AudioFilter().Process(silence, new AppConfig().NoiseGateDb);
        Equal(true, silence.All(x => x == 0));
        return Task.CompletedTask;
    }),
    ("Custom noise gate thresholds remain configurable", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-gate-{Guid.NewGuid():N}.json");
        try
        {
            ConfigManager.Save(new AppConfig { NoiseGateDb = -42 }, path);
            Equal(-42f, ConfigManager.Load(path).NoiseGateDb);
            ConfigManager.Save(new AppConfig { NoiseGateDb = -70 }, path);
            Equal(-70f, ConfigManager.Load(path).NoiseGateDb);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Custom modifier chords round-trip and require all keys", () =>
    {
        var key = HotkeyDefinition.FromDisplayName("Ctrl + Shift + Space");
        Equal("Ctrl + Shift + Space", HotkeyDefinition.DisplayName(key));
        var state = new HotkeyStateMachine(key);
        Equal(HotkeyTransition.None, state.Process(0xA2, true).Transition);
        Equal(HotkeyTransition.None, state.Process(0xA0, true).Transition);
        Equal(HotkeyTransition.Pressed, state.Process(0x20, true).Transition);
        Equal(HotkeyTransition.None, state.Process(0x20, true).Transition);
        Equal(HotkeyTransition.Released, state.Process(0xA0, false).Transition);
        Equal(false, state.Process(0xA2, false).Suppress);
        Equal(HotkeyTransition.None, state.Process(0x20, true, injected: true).Transition);
        return Task.CompletedTask;
    }),
    ("Shortcut recorder waits for release and Escape cancels", () =>
    {
        var capture = new HotkeyCaptureState();
        Equal(false, capture.Process(0xA2, true).Completed);
        Equal(false, capture.Process(0xA0, true).Completed);
        Equal(false, capture.Process(0x20, true).Completed);
        Equal(false, capture.Process(0x20, false).Completed);
        Equal(false, capture.Process(0xA2, false).Completed);
        var result = capture.Process(0xA0, false);
        Equal(true, result.Completed);
        Equal(HotkeyDefinition.FromDisplayName("Ctrl + Shift + Space"), result.Key!.Value);
        Equal(true, new HotkeyCaptureState().Process(0x1B, true).Completed);
        Equal<int?>(null, new HotkeyCaptureState().Process(0x1B, true).Key);
        var plain = new HotkeyCaptureState();
        plain.Process(0x41, true);
        Equal<int?>(null, plain.Process(0x41, false).Key);
        return Task.CompletedTask;
    }),
    ("Primary-first shortcut forwards a matched key release", () =>
    {
        var state = new HotkeyStateMachine(HotkeyDefinition.FromDisplayName("Ctrl + K"));
        Equal(false, state.Process(0x4B, true).Suppress);
        state.Process(0xA2, true);
        Equal(false, state.Process(0x4B, true).Suppress);
        Equal(false, state.Process(0x4B, false).Suppress);
        return Task.CompletedTask;
    }),
    ("Owner-drawn menu labels expose Microsoft accessibility metadata", () =>
    {
        using var label = new AccessibleMenuLabel("Whisper model");
        Equal(unchecked((int)0xAA0DF00D), Marshal.ReadInt32(label.Data));
        Equal(13, Marshal.ReadInt32(label.Data, 4));
        Equal("Whisper model", Marshal.PtrToStringUni(Marshal.ReadIntPtr(label.Data, 8))!);
        label.Dispose();
        Equal((nint)0, label.Data);
        return Task.CompletedTask;
    }),
    ("Microphone and per-mode preferences persist without changing defaults", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-controls-{Guid.NewGuid():N}.json");
        var config = new AppConfig { AudioDeviceId = "endpoint-123", EmailPrompt = "Use short paragraphs.", CodePrompt = "Preserve snake_case." };
        try { ConfigManager.Save(config, path); Equal(config, ConfigManager.Load(path)); }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Per-mode preferences follow the master and general preferences", () =>
    {
        var config = new AppConfig { CustomPrompt = "General preference.", EmailPrompt = "Email preference.", CodePrompt = "Code preference." };
        var prompt = PromptTemplates.For("email", config);
        Equal(true, prompt.StartsWith(DefaultPrompts.MasterPrompt, StringComparison.Ordinal));
        Equal(true, prompt.Contains("General preference."));
        Equal(true, prompt.Contains("Email preference."));
        Equal(false, prompt.Contains("Code preference."));
        return Task.CompletedTask;
    }),
    ("Pre-upgrade settings load with defaults for newly added optional fields", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-upgrade-{Guid.NewGuid():N}.json");
        const string legacy = """
            {"speech_model":"tiny.en","language":"en","noise_gate_db":-65,"hotkey":0,
             "backend":"chatgpt_oauth","refinement_model":"auto","mode":"auto","injection":"clipboard",
             "paste_delay_ms":100,"max_recording_seconds":120,"custom_prompt":"Preserve ticket IDs."}
            """;
        try
        {
            File.WriteAllText(path, legacy);
            var expected = new AppConfig { Backend = "chatgpt_oauth", SpeechModel = "tiny.en", CustomPrompt = "Preserve ticket IDs.", SetupFinished = true };
            Equal(expected, ConfigManager.Load(path));
            File.WriteAllText(path, legacy.Replace("\"speech_model\"", "\"audio_device_id\":null,\"email_prompt\":null,\"speech_model\""));
            Equal(expected, ConfigManager.Load(path));
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Known creation tools require review despite contradictory read-only hints", ConnectedChecks.Hints),
    ("ChatGPT low reasoning is limited to compatible ordinary cleanup requests", CleanupReasoningChecks.Routing),
    ("Automatic update enablement and action are separate persistent preferences", ReleaseUpdateChecks.Preferences),
    ("Release updates obey notify, download, install and manual restart policy", ReleaseUpdateChecks.Policy),
    ("Disabling automatic updates cancels owned work without restarting", ReleaseUpdateChecks.Cancellation),
    ("One-click install checks, downloads, reuses verified updates and blocks unsafe restarts", ReleaseUpdateChecks.OneClickInstall),
    ("Wake speech and prefix recognition block update restart before activation", ReleaseUpdateChecks.WakeEligibility),
    ("Installed sign-in startup resolves the stable launcher across updates", ReleaseUpdateChecks.StableStartup),
    ("Fast transcription preferences survive save and reload", TurnaroundChecks.Preferences),
    ("Fast transcription keeps full input, padding and long-recording context", TurnaroundChecks.DecodeBounds),
    ("Google AI Studio is a distinct valid refinement provider", GeminiApiChecks.Configuration),
    ("Google AI Studio uses header authentication and preserves requests, selections and test models", GeminiApiChecks.Transport),
    ("Google AI Studio rejects incomplete output, unsafe models and sanitized provider errors", GeminiApiChecks.Failures),
    ("Google API keys round-trip with Windows encryption, replacement and removal", GeminiApiChecks.KeyStorage),
    ("Connected access is opt-in and saved permissions survive restart", ConnectedChecks.Preferences),
    ("Connected untrusted rendering has bounded prefixes and output", ConnectedChecks.BoundedRendering),
    ("Connected previews require complete material fields and safe field names", ConnectedChecks.CompleteReview),
    ("Connected outcomes never trust model success or missing receipts", ConnectedChecks.Outcomes),
    ("Connected cancellation, account changes and lost receipts cannot replay writes", ConnectedChecks.Interruptions),
    ("Connected writes require an authoritative account", ConnectedChecks.Account),
    ("Connected discovery accepts large catalogs but retains tool-response limits", ConnectedChecks.LargeCatalog),
    ("Whisper non-speech markers never become user text", SpeechTextChecks.Markers),
    ("Connected RPC denies runtime prompts and cancels its owned process", ConnectedChecks.Transport),
    ("Connected loop pauses for a complete diff before any write", ConnectedChecks.Loop),
    ("Connected gateway enforces selection and confirmation without autonomous turns", ConnectedChecks.Gateway),
    ("Connected action diffs expose exact payload and block unsupported writes", ConnectedChecks.Diffs),
    ("Connected approval rejects stale, changed, double and closed requests", ConnectedChecks.Approvals),
    ("Bundled Edge voices prepare, repair and run without system Python", SmartAskChecks.BundledSpeech),
    ("Smart Ask rejects malformed routing and preserves insertion text", SmartAskChecks.Contracts),
    ("Smart Ask routes selected-context questions in one provider call", SmartAskChecks.Provider),
    ("Account changes retire conversational context before provider inference", SmartAskChecks.AccountIsolation),
    ("Smart Ask uses previous request context without bypassing routing", SmartAskChecks.SharedContext),
    ("Live snapshot copies bounded audio without consuming or aliasing the final recording", MicrophoneActivationChecks.DraftSnapshot),
    ("Live draft worker drains native inference and suppresses stale results", LiveTranscriptionChecks.Worker),
    ("Live draft defaults migrate, persist off, and show the newest Unicode text", LiveTranscriptionChecks.Configuration),
    ("Editor panel selects fresh general conversations and leaves attached contexts", EditorBridgeChecks.ConversationSelection),
    ("Explicit answer retrieval preserves full content and validates scope", ConversationChecks.FullAnswer),
    ("Conversation memory protects approvals and rolls back rejected over-budget turns", ConversationChecks.GuardedBounds),
    ("Conversation store retains scoped bounded memory and rejects stale results", ConversationChecks.Store),
    ("Answer follow-ups retain bounded conversation context and bypass cleanup thresholds", SmartAskChecks.FollowUp),
    ("Resumed toggle Ask stops on the next shortcut", RecordingGestureChecks.ResumedToggle),
    ("Editor hotkey handoffs reject released, duplicate and expired starts", EditorChecks.HotkeyHandoff),
    ("Answer speech and verbosity migrate, persist and reach providers", SmartAskChecks.AnswerPreferences),
    ("Smart Ask is opt-in and preferences survive migration", SmartAskChecks.Defaults),
    ("Spoken answers visibly fall back and cancellation never speaks again", SmartAskChecks.SpeechFallback),
    ("Pending microphone startup preserves Ask promotion and hotkey release", SmartAskChecks.RecordingGate),
    ("Edge speech helper uses private stdin and bounded cancellable binary output", SmartAskChecks.SpeechProcess),
    ("Automatic plugin updates preserve missing/current/newer installations", EditorPluginChecks.AutomaticUpdates),
    ("Canceled plugin installations release their gate and retry", EditorPluginChecks.AutomaticUpdateCancellation),
    ("Automatic and manual plugin installations serialize", EditorPluginChecks.AutomaticUpdateSerialization),
    ("Plugin maintenance retries failures and skips completed app versions", EditorPluginChecks.AutomaticUpdateStamp),
    ("Bundled editor plugin installs with safe arguments and verified status", EditorPluginChecks.Install),
    ("Editor plugins reject missing editors, downgrade, failures and cancellation", EditorPluginChecks.Failures),
    ("Editor workspace binding clears document context across authenticated peers and workspaces", EditorBridgeChecks.Binding),
    ("Editor pipe recovers after simultaneous incomplete handshakes", EditorBridgeChecks.Recovery),
    ("Editor pipe authenticates, routes bounded frames and removes discovery", EditorBridgeChecks.Channel),
    ("Code generation uses context and structured output despite cleanup threshold", EditorChecks.Provider),
    ("Exact editor shortcuts preserve scope and reject model native actions", EditorChecks.Shortcuts),
    ("Editor shortcuts reject constructs unsupported by the file format", EditorChecks.LanguagePolicy),
    ("Editor contracts reject malformed scope and retain code whitespace", EditorChecks.Contracts),
    ("Editor clarification accepts updated selection only in the unchanged source", EditorChecks.Retarget),
    ("Editor sessions reject stale, busy, duplicate and expired operations", EditorChecks.Sessions),
    ("Explicit history retry uses chosen model despite word threshold", HistoryRefinerChecks.ExplicitRetry),
    ("History retry preserves selected context and honors cancellation", HistoryRefinerChecks.SelectedContext),
    ("Recent dictations are bounded and deleted entries reject late retries", DictationHistoryChecks.Journal),
    ("Saved history loads before readiness and ordered clear/write operations", DictationHistoryChecks.StartupOrdering),
    ("Opt-in history is encrypted and can be removed from disk", DictationHistoryChecks.EncryptedPersistence),
    ("Toggle gestures retain nested Ask promotion and reset after cancellation", RecordingGestureChecks.Routing),
    ("Optional silence stop respects timing, unavailable input and disabled state", RecordingGestureChecks.Silence),
    ("Writing dictionary and snippets respect boundaries and explicit voice intent", WritingToolsChecks.TextRules),
    ("App profiles and writing preferences inherit and round-trip", WritingToolsChecks.ProfilesAndPersistence),
    ("Ambiguous or invalid writing preferences are rejected", WritingToolsChecks.Validation),
    ("Embedded inference uses the selected local model without account access", LocalModelChecks.SelectedInference),
    ("Fresh installs disable cleanup and saved provider/model choices persist", LocalModelChecks.DefaultsAndPersistence),
    ("Local model transfer reports progress and reuses verified files", LocalModelChecks.TransferAndCache),
    ("Local model integrity and cancellation preserve existing files and allow retry", LocalModelChecks.IntegrityAndCancellation),
    ("Concurrent local preparation downloads once per path", LocalModelChecks.ConcurrentPreparation),
    ("A completed AI reply proves connection even if the model adds punctuation", ConnectionTestChecks.CompletedReply),
    ("Local AI connection checks never download missing or invalid weights", ConnectionTestChecks.LocalNoDownload),
    ("Connection test sends fixed input using the draft model and reports actual response model", ConnectionTestChecks.ExplicitModel),
    ("Connection test Auto performs real catalog resolution and inference", ConnectionTestChecks.AutoModel),
    ("Connection test rejects bad replies, disabled AI, cancellation and provider failures", ConnectionTestChecks.Failures),
    ("Cleanup threshold counts words and preserves default, disabled and legacy settings", RefinementThresholdChecks.PolicyAndPersistence),
    ("Cleanup threshold respects eight-word boundary and explicit AI intent", RefinementThresholdChecks.BoundaryAndExemptions),
    ("Short ordinary dictation bypasses every AI provider before authentication", RefinementThresholdChecks.Bypass),
    ("Gemini routes dictation, selected edits and Ask AI through text-only Flash", AntigravityChecks.Routing),
    ("Gemini refuses global customizations and alternate API billing", AntigravityChecks.Isolation),
    ("Gemini rejects unsupported, incomplete and denied results", AntigravityChecks.Failures),
    ("Gemini subscription configuration is supported and remains opt-in", () =>
    {
        (new AppConfig { Backend = "gemini_antigravity" }).Validate();
        Equal("none", new AppConfig().Backend);
        Equal(ChatGptAccountAction.ManageAccount, RefinementAuthentication.ActionFor("gemini_antigravity", true));
        return Task.CompletedTask;
    }),
    ("ChatGPT authentication offers one action only for its backend", () =>
    {
        Equal(ChatGptAccountAction.SignIn, RefinementAuthentication.ActionFor("chatgpt_oauth", false));
        Equal(ChatGptAccountAction.ManageAccount, RefinementAuthentication.ActionFor("chatgpt_oauth", true));
        foreach (var backend in new[] { "none", "embedded" })
        foreach (var signedIn in new[] { false, true })
            Equal(ChatGptAccountAction.None, RefinementAuthentication.ActionFor(backend, signedIn));
        foreach (var backend in new[] { "chatgpt_oauth", "claude_code", "none", "embedded" })
        foreach (var signedIn in new[] { false, true })
        foreach (var busy in new[] { false, true })
        foreach (var action in Enum.GetValues<ChatGptAccountAction>())
        {
            var expected = !busy && (backend is "chatgpt_oauth" or "claude_code") &&
                (signedIn ? action is ChatGptAccountAction.Reconnect or ChatGptAccountAction.SignOut : action == ChatGptAccountAction.SignIn);
            Equal(expected, RefinementAuthentication.CanRequest(backend, signedIn, busy, action));
        }
        return Task.CompletedTask;
    }),
    ("Windows login startup safely registers and rolls back failed saves", WindowsLoginStartupChecks.Run),
    ("Login startup defaults off and preserves explicit saved choices", () =>
    {
        Equal(false, new AppConfig().StartOnLogin);
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-startup-{Guid.NewGuid():N}.json");
        try
        {
            var enabled = new AppConfig { StartOnLogin = true };
            ConfigManager.Save(enabled, path);
            Equal(enabled, ConfigManager.Load(path));
            var json = File.ReadAllText(path);
            json = json.Replace("\"start_on_login\": true,", "");
            Equal(false, json.Contains("start_on_login"));
            File.WriteAllText(path, json);
            Equal(false, ConfigManager.Load(path).StartOnLogin);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Configuration round-trips through source generated JSON", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-test-{Guid.NewGuid():N}.json");
        var config = new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "custom-model", CustomPrompt = "Preserve café and 😀" };
        try
        {
            ConfigManager.Save(config, path); Equal(config, ConfigManager.Load(path));
            Equal(true, File.ReadAllText(path).Contains("speech_model"));
            Equal(false, JsonSerializer.IsReflectionEnabledByDefault);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Saved legacy ChatGPT model migrates to account model discovery", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-legacy-{Guid.NewGuid():N}.json");
        try
        {
            ConfigManager.Save(new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "gpt-5.1-codex" }, path);
            Equal("auto", ConfigManager.Load(path).RefinementModel);
            ConfigManager.Save(new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "custom-model" }, path);
            Equal("custom-model", ConfigManager.Load(path).RefinementModel);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Invalid and non-finite settings are rejected", () =>
    {
        Throws<ArgumentException>(() => (new AppConfig { NoiseGateDb = float.NaN }).Validate());
        Throws<ArgumentException>(() => (new AppConfig { Hotkey = 65 }).Validate());
        Throws<ArgumentException>(() => (new AppConfig { Backend = "typo" }).Validate());
        Throws<ArgumentException>(() => (new AppConfig { MaxRecordingSeconds = 0 }).Validate());
        return Task.CompletedTask;
    }),
    ("SSE supports CRLF, comments, multiline data and Unicode", async () =>
    {
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes(": keepalive\r\nevent: test\r\ndata: café 😀\r\ndata: second line\r\n\r\ndata: [DONE]\r\n\r\n"));
        var events = new List<string>();
        await foreach (var data in SseParser.ReadAsync(stream)) events.Add(data);
        Equal(2, events.Count); Equal("café 😀\nsecond line", events[0]); Equal("[DONE]", events[1]);
    }),
    ("SSE discards an interrupted final event", async () =>
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data: incomplete"));
        int count = 0;
        await foreach (var _ in SseParser.ReadAsync(stream)) count++;
        Equal(0, count);
    }),
    ("SSE cancellation stops a pending read", async () =>
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data: test\n\n"));
        await ThrowsAsync<OperationCanceledException>(async () => { await foreach (var _ in SseParser.ReadAsync(stream, ct.Token)) { } });
    }),
    ("Included Tiny Q5 English resolves without network or a duplicate cache", BundledModelChecks.UsesBundle),
    ("Included speech model preserves valid cache priority and repairs corrupt cache", BundledModelChecks.CachePriority),
    ("Missing or corrupt included models retain verified download recovery", BundledModelChecks.Recovery),
    ("Included model resolution respects cancellation", BundledModelChecks.Cancellation),
    ("Pill placement preserves valid monitors and recovers offscreen coordinates", PillLayoutChecks.Placement),
    ("Pill follows the focused monitor while preserving relative placement", PillLayoutChecks.FocusedMonitor),
    ("First-launch setup preserves skip, pending and legacy preferences", OnboardingChecks.Preferences),
    ("Pill position persists without changing other settings or legacy defaults", PillLayoutChecks.Persistence),
    ("Model integrity check rejects changed bytes", async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-model-{Guid.NewGuid():N}");
        try
        {
            var original = Encoding.UTF8.GetBytes("fixture model");
            var expected = Convert.ToHexString(SHA256.HashData(original));
            await File.WriteAllBytesAsync(path, original);
            Equal(true, await ModelManager.VerifyAsync(path, expected, default));
            await File.WriteAllTextAsync(path, "modified model");
            Equal(false, await ModelManager.VerifyAsync(path, expected, default));
        }
        finally { File.Delete(path); }
    }),
    ("Local dictation does not make a network request", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => throw new Exception("Unexpected network request")));
        using var auth = new ChatGptAuthManager(http);
        var result = await new UnifiedRefiner(http, auth).RefineAsync("Hello world.", "notepad", new AppConfig { Backend = "none" }, _ => { }, default);
        Equal("Hello world.", result);
    }),
    ("ChatGPT auto selects an available visible model and caches discovery", async () =>
    {
        var catalogCalls = 0;
        var inferenceCalls = 0;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                catalogCalls++;
                Equal("/backend-api/codex/models", request.RequestUri!.AbsolutePath);
                Equal(true, request.RequestUri.Query.Contains("client_version="));
                return new(HttpStatusCode.OK) { Content = new StringContent("""
                    {"models":[
                      {"slug":"hidden-model","visibility":"hide","priority":0,"supported_in_api":true},
                      {"slug":"later-model","visibility":"list","priority":10,"supported_in_api":true},
                      {"slug":"available-model","visibility":"list","priority":1,"supported_in_api":true}
                    ]}
                    """) };
            }
            inferenceCalls++;
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal("available-model", payload.RootElement.GetProperty("model").GetString());
            Equal(false, payload.RootElement.GetProperty("store").GetBoolean());
            Equal(DefaultPrompts.MasterPrompt, payload.RootElement.GetProperty("instructions").GetString());
            return Sse("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello world.\"}\n\ndata: {\"type\":\"response.completed\"}\n\n");
        }));
        var refiner = new UnifiedRefiner(http, new FixtureSessionProvider());
        var config = new AppConfig { Backend = "chatgpt_oauth", MinimumAiWords = 0 }; // Exercise model discovery even with the short protocol fixture.
        Equal("Hello world.", await refiner.RefineAsync("hello world", "notepad", config, _ => { }, default));
        Equal("Hello world.", await refiner.RefineAsync("hello world", "notepad", config, _ => { }, default));
        Equal(1, catalogCalls); Equal(2, inferenceCalls);
    }),
    ("ChatGPT unavailable-model errors explain how to recover without exposing response content", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"type":"invalid_request_error","code":"model_not_found","message":"sensitive provider body"}}""")
        }));
        try
        {
            await new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("hello", "notepad", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "retired-model", MinimumAiWords = 0 }, _ => { }, default);
            throw new Exception("Expected failure");
        }
        catch (InvalidOperationException ex)
        {
            Equal(true, ex.Message.Contains("auto"));
            Equal(false, ex.Message.Contains("sensitive provider body"));
        }
    }),
    ("Empty ChatGPT catalog stops before inference", async () =>
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method != HttpMethod.Get) throw new Exception("No supported model; inference must not be called.");
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[]}") };
        }));
        await ThrowsAsync<InvalidOperationException>(() => new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("hello", "notepad", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "auto", MinimumAiWords = 0 }, _ => { }, default));
    }),
    ("ChatGPT streaming assembles only completed text", async () =>
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Equal("chatgpt.com", request.RequestUri!.Host);
            return Sse("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello \"}\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"world.\"}\n\ndata: {\"type\":\"response.completed\"}\n\n");
        }));
        var result = await new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("hello world", "outlook", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "test", MinimumAiWords = 0 }, _ => { }, default);
        Equal("Hello world.", result);
    }),
    ("Interrupted refinement never returns a partial result", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => Sse("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Partial text\"}\n\n")));
        await ThrowsAsync<IOException>(() => new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("source", "notepad", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "test", MinimumAiWords = 0 }, _ => { }, default));
    }),
    ("Token-limited refinement is rejected even when DONE follows", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => Sse("data: {\"type\":\"response.incomplete\"}\n\ndata: [DONE]\n\n")));
        await ThrowsAsync<InvalidDataException>(() => new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("source", "notepad", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "test", MinimumAiWords = 0 }, _ => { }, default));
    }),
    ("Refinement HTTP errors do not expose provider response bodies", async () =>
    {
        using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive response content") }));
        try { await new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("source", "notepad", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "test", MinimumAiWords = 0 }, _ => { }, default); throw new Exception("Expected failure"); }
        catch (InvalidOperationException ex) { Equal(true, ex.Message.Contains("401")); Equal(false, ex.Message.Contains("sensitive")); }
    }),
    ("Retired Groq settings migrate without losing user preferences", () =>
    {
        Throws<ArgumentException>(() => (new AppConfig { Backend = "groq" }).Validate());
        var path = Path.Combine(Path.GetTempPath(), $"sharpvoice-retired-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AppConfig { Backend = "chatgpt_oauth", NoiseGateDb = -71, CustomPrompt = "Preserve my style", AskHotkey = 0x78 };
            var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.AppConfig)
                .Replace("chatgpt_oauth", "groq");
            File.WriteAllText(path, json.Replace("\"refinement_model\": \"auto\"", "\"refinement_model\": \"llama-3.3-70b-versatile\""));
            Equal(original, ConfigManager.Load(path));
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }),
    ("Foreground process names select the expected writing mode", () =>
    {
        Equal("email", PromptTemplates.DetectMode("OUTLOOK")); Equal("code", PromptTemplates.DetectMode("Code"));
        Equal("casual", PromptTemplates.DetectMode("slack")); Equal("prose", PromptTemplates.DetectMode("notepad"));
        return Task.CompletedTask;
    }),
    ("Win32 structures have the Windows x64 ABI layout", () =>
    {
        Equal(8, IntPtr.Size);
        Equal(40, Marshal.SizeOf<Win32.Input>());
        Equal(8, Marshal.OffsetOf<Win32.Input>(nameof(Win32.Input.Data)).ToInt32());
        Equal(976, Marshal.SizeOf<Win32.NotifyIconData>());
        Equal(80, Marshal.SizeOf<Win32.WindowClass>());
        Equal(72, Marshal.SizeOf<Win32.PaintStruct>());
        Equal(72, Marshal.SizeOf<Win32.GuiThreadInfo>());
        return Task.CompletedTask;
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; received {actual}.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
static HttpResponseMessage Sse(string data) => new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "text/event-stream") };

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
}
sealed class FixtureSessionProvider : IChatGptSessionProvider
{
    public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => Task.FromResult(new ChatGptSession("test-token", "test-refresh", "test-account", DateTimeOffset.UtcNow.AddHours(1)));
}
sealed class RejectSessionProvider : IChatGptSessionProvider
{
    public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => throw new Exception("Empty transcription must not access a session.");
}
sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
}

sealed class TrackingFloatPool : System.Buffers.ArrayPool<float>
{
    public int Returns; public bool Cleared;
    public override float[] Rent(int minimumLength) => new float[minimumLength + 16];
    public override void Return(float[] array, bool clearArray = false)
    {
        Returns++;
        if (clearArray) Array.Clear(array);
        Cleared = clearArray && array.All(x => x == 0);
    }
}
