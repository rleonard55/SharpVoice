# SharpVoice Development

[← Back to the README](../README.md) · [Release packaging](releasing.md) · [User guide](user-guide.md)

Build requirements: Windows x64, the .NET 10 SDK, and an AVX2/FMA-capable CPU for the accelerated native runtime. Native AOT additionally requires Visual Studio C++ build tools and the Windows SDK.

Run from source:

```powershell
dotnet run --project src/SharpVoice.App/SharpVoice.App.csproj -c Release -p:PublishAot=false
```

## Build and checks

```powershell
# Portable logic and HTTP-fixture checks; no microphone, model download, or live credentials needed
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -c Release

# Windows-only native checks (the audio check briefly captures, saves no audio)
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-native-smoke
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-audio-smoke
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-device-smoke

# Microphone icon transitions and embedded sound playback (briefly plays the cues)
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-feedback-smoke
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-sound-smoke

# Five-second background-level sample; stay quiet (prints levels, saves no audio)
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-room-noise

# Verify dropdown display and Apply preserve default and custom settings (no profile writes)
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-settings-smoke defaults
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-settings-smoke custom

# Clipboard-to-RichEdit transfer check (hidden window; replaces clipboard with fixture text)
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --windows-clipboard-smoke

# Optional live check using SharpVoice's own saved session and a fixed test transcript
dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -- --chatgpt-live-smoke

# Windows self-contained prototype: artifacts/prototype/SharpVoice.exe
powershell -File scripts/Build.ps1

# Native AOT: requires Windows x64 and Visual Studio C++ build tools + Windows SDK
powershell -File scripts/Build.ps1 -Aot
```

The AOT build is staged at **artifacts/aot-update/SharpVoice.exe**, with an executable-only copy at **artifacts/portable/SharpVoice.exe**. Exit the running app and use the launcher to install the staged update. Managed self-contained output still needs its publish folder. AOT embeds the CPU libraries and extracts a verified versioned cache; model downloads remain external. Linux cannot cross-publish Windows Native AOT.

The app and test runner now publish with **zero AOT/trim warnings**, enforced as build errors. The managed inference bindings use [pinned source builds with narrow AOT fixes](../vendor/README.md); native runtime versions are unchanged. See [AOT compatibility evidence](aot-compatibility.md) for the fixes, regression checks and intentionally failing guard fixture. The CPU baseline is explicit because [Whisper.net 1.9.1's AVX probe folds to false in baseline AOT builds](https://github.com/sandrohanea/whisper.net/issues/538). See [verification results](verification.md).

## Implemented

- Seven-module solution: Core, Audio, Speech, Refiner, Platform.Windows, UI, App.
- Single instance, native message pump, low-level hold-key hook with repeat/injected-event filtering.
- Event-driven shared-mode WASAPI, Windows engine resampling to mono 16 kHz PCM, 80 Hz high-pass, block RMS gate, optional Keep Ready 250 ms rolling pre-roll and 800-frame (50 ms) post-roll. On Hotkey opens capture only on demand; explicit preview also temporarily opens it. Recording is bounded (120 seconds by default), and silence skips Whisper inference.
- Whisper.net CPU inference; tiny/base/small English model downloads with pinned SHA-256 checks and atomic installation; standard and Q5 variants, key-down preloading, and factory release after ten minutes of inactivity.
- PKCE S256, random state validation, bounded IPv4 loopback callback, three-minute login timeout, DPAPI session storage and refresh.
- UTF-8 SSE parsing, ChatGPT, optional Claude Code and local Qwen refinement, foreground-process writing-mode selection, atomic clipboard paste or paced Unicode insertion, and cancellation.
- Flat, transparent microphone tray icons use neutral gray, recording red and warning amber for small-size clarity; larger app/executable icons retain the faceted branding.
- AI Refinement choices show flat provider icons in both the list and selected field, using monochrome marks matched to the current theme.
- Settings buttons, fields, dropdowns and sliders provide theme-aware hover feedback, with distinct keyboard focus and disabled states.
- OS-themed native tray menu with Settings, pause/resume, Copy and Exit, draggable non-activating HUD with remembered placement with audio levels/transcription/refinement previews, and six-tab settings with microphone controls, shortcut recording and per-mode instructions.

## Prototype limits / next work

- Windows microphone capture, hooks, sign-in, tray/HUD rendering, text insertion and device disconnect behavior require the [manual smoke test](windows-smoke-test.md).
- Microphones are listed when Settings is created; use **Refresh microphones** after plugging one in and Apply to select/reconnect it. Direct2D rendering and one-file delivery are implemented; exact latency/CPU/memory targets and strict static-link packaging remain acceptance gaps. See the [original-spec checklist](spec-completion.md).
- Live transcription shows provisional local drafts during recording; the full recording is transcribed after Stop. Draft cadence depends on model and hardware, and draft words may change.
- OpenAI Platform was excluded at your request. Optional embedded LLamaSharp/Qwen is available; its speed and editing quality are limited.
- Continuous capture is necessary for true pre-roll, so idle CPU is not literally zero. Hardware shared-mode packet cadence and OS scheduling determine latency; the 50 ms tail counts audio frames rather than promising exact wall-clock timing. Silence does not unload the microphone; tray Pause stops active capture.
- Post-insertion trimming lowers resident working set, while committed memory remains allocated. Startup, capture and inference samples are recorded in [performance.md](performance.md); strict performance targets remain unaccepted.
- This is not a production-ready release. Test in Notepad before dictating into important applications.

## Upstream references

For unreliable wake phrases, run `Launch-Wake-Diagnostics.cmd`. See the [diagnostic utility guide](wake-diagnostics.md) for guided tests and report export. Audio recording in reports is optional and off by default.

- [Whisper.net usage and runtime layout](https://github.com/sandrohanea/whisper.net)
- [Whisper model files / LFS SHA-256 metadata](https://huggingface.co/api/models/ggerganov/whisper.cpp/tree/main)
- [Microsoft IAudioClient contract](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient)
- [Upstream Codex browser authorization flow](https://github.com/openai/codex/blob/main/codex-rs/login/src/server.rs)

## Specification acceptance and measurements

See [performance measurements](performance.md), [third-party notices](third-party-notices.md), and [spec-completion.md](spec-completion.md) for implementation evidence and explicit remaining acceptance gaps. The updated executable bundles native inference libraries; it still creates DLL cache and model files on disk. AOT/trim warnings are treated as errors; strict native publication is verified for the supported application paths.

After publishing the AOT test runner, run `scripts/measure-performance.ps1` to measure idle capture, paused CPU/memory, model loading, inference, and working-set trimming. Results go to `artifacts/performance.txt`. No microphone audio is saved. Performance numbers depend on hardware, model, transcript length and provider latency.
