# Original specification completion

Source: `Data/Spec.txt`. Later user choices take precedence: Ctrl+Alt, tiny.en, ChatGPT OAuth/auto, embedded MasterPrompt, clipboard insertion, −65 dB gate, microphone icons/chimes, tray-only startup, and OS light/dark themes. **OpenAI Platform was explicitly excluded by the user on 2026-10-31. Groq was removed at the user’s request on 2026-11-01.**

| Requirement | Implementation / acceptance evidence |
|---|---|
| .NET 10 Native AOT, generated JSON and Win32 imports | Implemented. App and tests now publish with zero AOT/trim warnings under a strict errors gate; pinned managed binding source fixes and a negative reachability probe are recorded in [aot-compatibility.md](aot-compatibility.md). |
| WASAPI mono 16 kHz, RMS gate, 80 Hz high-pass | Implemented and native capture smoke tested. Shared-mode packet timing is controlled by Windows; an exact 10 ms hardware cadence is not guaranteed. |
| 250 ms pre-roll / 800-frame post-roll | Implemented; tail is measured in captured frames, not wall-clock delay. |
| Pooled audio / concurrent ring / zero-copy handoff | Pooled ring and PCM conversion buffer; versioned single-writer snapshots pass concurrent wrap checks. Recording owns one pooled buffer transferred directly to Whisper as ReadOnlyMemory; no List growth/final ToArray. Recorder state still uses a short lock. PCM conversion and pre-roll copying remain necessary; the whole pipeline is not literally zero-copy. |
| Whisper tiny/base/small and quantized models | Six choices: standard English models plus Q5_1 variants. Official LFS SHA-256 values pinned; tiny Q5 real inference verified. Standard tiny remains default. |
| First-run downloads and integrity | Atomic verified Whisper and optional Qwen downloads; no partial file installation. |
| Idle unload and hotkey reload | Ten-minute unload retained. Loading starts on key-down and overlaps speech. Latest AOT Q5 load was 98.45 ms; earlier runs included 109.38 ms and 161.74 ms. A universal <150 ms reload guarantee is not accepted. |
| ChatGPT PKCE, loopback, DPAPI, refresh, streaming | Implemented; TCP loopback replaces HttpListener. Account catalog replaces retired fixed default model. |
| Groq | Removed from scope and implementation at user request; legacy profiles migrate to ChatGPT/auto. |
| Claude (added scope) | Official native Claude Code bridge; Anthropic owns subscription sign-in and credentials. Requires CLI 2.1.293+, Auto uses Haiku, account controls and actual model reporting included. Direct third-party Claude.ai OAuth is not offered. The installed CLI was updated from 2.0.72 to 2.1.316; subscription status and a fixed live production-refiner request passed, reporting claude-haiku-5-5. Full desktop insertion/provider-failure acceptance remains open. |
| OpenAI Platform | Removed from requested scope by user; no key created and no Platform calls added. |
| Optional embedded local LLM | LLamaSharp 0.30.0 / CPU Qwen2.5-0.5B-Instruct Q4_K_M implemented. First-use model download is 491,400,032 bytes. Full-master-prompt fixture passed managed; native acceptance recorded in verification log. Weights/context disposed after each request. Small-model output quality is limited. |
| Foreground checks and insertion | Implemented. Clipboard default; optional paced typing. Complete text retained on provider/insertion failure. |
| Settings / device / gate / meter / prompts / hotkey recorder | Implemented; native persistence/capture checks and themed visual review. |
| Start on sign-in (added scope) | Optional Off/On preference in Insertion. Per-user Run registration on Apply; failed configuration save restores previous registry value. Off by default; tray-only startup and quiet duplicate --startup launch. Full Windows sign-out/sign-in acceptance remains manual. |
| Tray menu / icons | Simplified per user request: Settings, pause/resume, Copy and Exit, with accessible icons. Models and account actions live in Settings; cancellation appears there only during active dictation. |
| Direct2D HUD | Compact rounded pill with microphone badge, concrete AI model label, DirectWrite text and waveform bars. Click-through layered window retained; GDI fallback after unavailable rendering. Both palettes verified with actual offscreen pixels. Translucent; no Windows Composition backdrop blur. |
| Post-paste working-set trim | Implemented with K32EmptyWorkingSet and supported nonblocking optimized GC. Aggressive GC cannot be combined with the nonblocking parameters proposed in the spec. Working-set trimming does not release committed model memory. |
| Single standalone executable | Inference runtime libraries and designated Microsoft C++/OpenMP redistributables compressed into executable; SHA-256-verified extraction to `%USERPROFILE%/.sharpvoice/native/<bundle hash>`. No installed .NET/Python required. Native DLL cache and model files still exist on disk. Strict static linking/no runtime files is not implemented. |
| Size / memory / CPU / latency / startup targets | See performance measurements. These are acceptance criteria, not guaranteed by an AOT build. Cloud latency and local model inference prevent a universal <350 ms turnaround claim. |
| Comprehensive Windows verification | Automated behavior, capture, HUD, settings and real inference checks exist. Physical unplug/sleep, Explorer restart, multiple DPI settings, screen readers, full app dictation and long-running use still need interactive acceptance. |

## Upstream metadata and APIs

- [Whisper quantized model metadata](https://huggingface.co/api/models/ggerganov/whisper.cpp/tree/main?limit=100) and [Tiny Q5 file](https://huggingface.co/ggerganov/whisper.cpp/blob/main/ggml-tiny.en-q5_1.bin).
- [Qwen model and license](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF), [LLamaSharp](https://github.com/SciSharp/LLamaSharp).
- [Direct2D DC rendering](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/nn-d2d1-id2d1dcrendertarget), [working-set trimming](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset).

The functional scope is broader than the original prototype. Full specification acceptance remains open for the explicit deviations, performance targets, and interactive checks above. AOT warning cleanup is verified for the supported app/test graphs.

Gemini addition (user-approved): optional official Antigravity CLI subscription integration (`gemini_antigravity`) supports all three voice modes with Auto Flash, explicit Gemini IDs and Google-owned interactive account controls. No direct Google token collection/API-key fallback. Unlike Claude, Antigravity can retain its own conversations/history; this optional runtime is outside SharpVoice's memory-only transcript guarantee and is disclosed in Settings/README. Global customization preflight and complete-result checks limit the adapter to text processing.
