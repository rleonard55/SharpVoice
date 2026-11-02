# Wake Phrases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Optional local activation with “Hey SharpVoice” for Ask AI and “Hey SharpVoice Dictate” for dictation, including uninterrupted speech and automatic silence stop.

**Architecture:** A dedicated sherpa keyword worker receives bounded raw audio from the existing WASAPI recorder. Detection reserves one provisional session with a gapless audio handoff; the longest matching wake prefix resolves the mode before provider routing. Native AOT runtime/model preparation integrates with the existing one-file distribution and microphone lifecycle.

**Tech Stack:** C#/.NET, native Win32 UI, WASAPI, sherpa-onnx C API, verified model-compatible SentencePiece UNIGRAM tokenization, existing Whisper final/live transcription.

**Spec:** `docs/superpowers/specs/2026-11-01-wake-phrases-design.md`

## Global Constraints

- Wake listening is optional and disabled on install and when migrating existing configurations. On Hotkey remains the normal microphone default.
- User-selected defaults: **Hey SharpVoice** starts Ask AI; **Hey SharpVoice Dictate** starts ordinary dictation.
- Wake-owned recordings stop after **2 seconds of silence**, adjustable from 1–10 seconds.
- Keep an **8-second maximum** in-memory audio ring with absolute sample positions and a capture epoch.
- No user-installed Python/helpers, idle Whisper transcription, cloud wake detection, extra microphone session, or changes to tool approval/editor preview requirements.
- Native AOT and standalone packaging must work. No user app/editor restart or real microphone/clipboard/accounts/actions in automated tests.
- Execute natively in the shared authorized workspace, as established by the existing workspace ledger. Do not create worktrees, commits or git mutations in this untracked project within a parent repository. Record task evidence in a wake ledger instead.

## Review Focus

1. Overlapping default phrases with AI disabled: Dictate must not be rejected as Ask AI before the suffix is resolved (task 4).
2. A delayed selection snapshot/device stall: continuous speech must remain gapless or be rejected clearly, never partially pasted (tasks 3/4).
3. Whisper omits or punctuates the wake phrase: preserve request words and choose the confirmed mode without leaving the prefix in output (tasks 2/4).
4. Pause, TTS, rapid config replacement or stale detector callbacks: no self-trigger, old-device audio or native-use-after-dispose (tasks 3/4).
5. A canceled/corrupt first-time model download: existing hotkeys remain functional and wake status must not say Ready (tasks 1/5).

## File Map

- `src/SharpVoice.Core/WakePhrases.cs`: immutable detection/result/status contracts, validation, normalized longest-prefix stripping and resolution.
- `src/SharpVoice.Core/WakeSession.cs`: operation origin, single-session reservation, same-onset confirmation and shortcut-stop policy independent of UI/native handles.
- `src/SharpVoice.Core/AppConfig.cs`: saved wake options, explicit migration defaults, equality and validation.
- `src/SharpVoice.Speech/WakeModelManager.cs`: pinned assets, streaming verified model installation and progress.
- `src/SharpVoice.Speech/WakeKeywordDetector.cs`, `WakeNative.cs`, `WakeTokenizer.cs`: worker/native ownership, inference and model-compatible phrase encoding.
- `src/SharpVoice.Speech/SharpVoice.Speech.csproj`, `Directory.Build.targets`, `docs/licenses/`: pinned runtime and tokenizer, bundled native dependencies/notices.
- `src/SharpVoice.Audio/WakeAudioBuffer.cs`, `WasapiAudioRecorder.Wake.cs`: timestamped raw/gated rings, bounded handoff and wake capture demand.
- `src/SharpVoice.Audio/WasapiAudioRecorder.cs`: feed wake buffers before destructive gating and append handoff atomically under recorder ownership.
- `src/SharpVoice.App/SharpVoiceApp.Wake.cs`: wake preparation, state transitions, generation checks, provisional capture and final routing.
- Existing App lifecycle partials: wire suspension/resume and transcript cleanup at common boundaries without altering extension protocol.
- `src/SharpVoice.UI/NativeDesktop.Wake.cs`: themed compact wake preferences and runtime preparation status; existing Settings/Help/Presentation files route entry/help/status.
- Tests: `WakePhraseChecks.cs`, `WakeDetectorChecks.cs`, `WakeCaptureChecks.cs`, `WakeSessionChecks.cs`, `WindowsHelpSmoke.Wake.cs`, test-runner registration; extend existing microphone checks.
- Delivery docs: README, verification, Windows smoke test, `docs/superpowers/plans/2026-11-01-wake-phrases-ledger.md`.

## Shared Verification Commands

Run Windows commands sequentially from the workspace; use the established escalation when WSL cannot launch Windows tools. Capture each RED/GREEN result in `.build-cache/wake-*.log`.

Build: `dotnet build SharpVoice.sln -p:PublishAot=false -p:UseSharedCompilation=false -m:1 --nologo --artifacts-path .build-cache/windows -p:RestorePackagesPath=.build-cache/nuget`

Managed checks: `.build-cache/windows/bin/SharpVoice.Tests/debug/SharpVoice.Tests.exe`

Native settings checks: `.build-cache/windows/bin/SharpVoice.Tests/debug/SharpVoice.Tests.exe --windows-help-smoke`

Publish tests: `cmd.exe /d /c .build-cache\\publish-aot-tests.cmd`; run the resulting `artifacts/tests-aot/SharpVoice.Tests.exe`. Confirm current helper output paths before execution.

Publish app only after test subprocesses exit: `cmd.exe /d /c .build-cache\\publish-aot-insertion.cmd`. Guarded stage: `powershell.exe -NoProfile -File .build-cache/answer-reply-stage.ps1` after inspecting its path/hash checks. Never overwrite the live app directly.

---

### Task 1: Prove and package the dedicated detector

**Files:** New Speech WakeModelManager/WakeKeywordDetector/WakeNative/WakeTokenizer; Core WakePhrases contract declarations; Speech project, bundle targets and notices; `tests/SharpVoice.Tests/WakeDetectorChecks.cs` and Program registration.

**Interfaces:**
- Core `public readonly record struct WakeDetection(VoiceMode Mode, long StartSample, long Epoch)`.
- Core `public sealed record WakePreparation(string State, long DownloadedBytes, long? TotalBytes, string Message)`.
- Speech `WakeModelManager(HttpClient http, string? root = null)`; `Task<string> EnsureAsync(Action<WakePreparation> progress, CancellationToken ct)` returns the verified model directory.
- Speech `WakeKeywordDetector(string modelDirectory, string dictationPhrase, string askPhrase)` implements `IAsyncDisposable`; `event Action<WakeDetection>? Detected`; `bool Enqueue(ReadOnlySpan<float> samples, long firstSample, long epoch)` copies into bounded owned storage without waiting for inference; `void Reset(long epoch)` invalidates queued generations.
- Tokenizer internal `string Encode(string phrase)` returns exact sherpa keyword-file token strings, with stable phrase IDs mapping results to modes.

- [x] Add `WakeDetectorChecks.Tokenization` asserting branded/spaced defaults encode identically and match upstream SentencePiece-produced reference tokens; reject unsupported or empty token sequences. Add `Assets` cases for wrong hash, truncated/oversize asset, cancellation, clean retry and no Ready event after failure. Register and run the build/check command to observe RED.
- [x] Pin an actual Windows x64 sherpa release and compatible tokenizer from primary upstream releases. Prove model-compatible BPE in managed and AOT processes with prerecorded audio before app integration. Record source URLs, versions, licenses and SHA-256 hashes; do not substitute approximate tokenization or a user Python setup. Implement direct asset download with bounded sizes/hashes and atomic verified promotion; use an injectable HTTP handler/temp root for tests.
- [x] Implement one sequential native-handle owner with a bounded queue of at most 1 second (16,000 samples), reset-on-discontinuity and generation-tagged detection. Native C structs must match the pinned header exactly. A worker failure raises an unavailable status through its owner, never an exception on the audio thread. Drain before destroying native handles.
- [x] Bundle dependencies under a dedicated `wake` subdirectory through NativeRuntimeBundle, preserving separate Whisper/LLama runtimes. Add a `--wake-detector-fixture <model-directory> <fixture-manifest>` runner using prerecorded positives, negatives, quiet speech and both continuous default phrases. Assert both modes and onset positions, native disposal after reset/cancel, queue overflow rejection and no stale-epoch callback acceptance. Keep sample assets/expected results locally reproducible and licensed.
- [x] Run build, managed checks, detector fixture and AOT detector fixture. Measure CPU, private memory and detector activation delay on the fixture machine; record evidence before marking this task complete. If native loading/tokenization cannot be proven, stop detector integration and report the specific blocker; do not ship a placeholder or silently use Whisper for idle detection.

### Task 2: Configuration, overlapping prefixes and session policy

**Files:** Core AppConfig/WakePhrases/WakeSession; tests WakePhraseChecks/WakeSessionChecks and Program registration.

**Interfaces:**
- AppConfig `bool WakePhrasesEnabled = false`, `string DictationWakePhrase = "Hey SharpVoice Dictate"`, `string AskWakePhrase = "Hey SharpVoice"`, `int WakeSilenceSeconds = 2` (init properties).
- Core `public readonly record struct WakeTranscript(VoiceMode Mode, string Text)`; `WakePhrases.Validate(AppConfig config)`; `WakeTranscript WakePhrases.Resolve(AppConfig config, string transcript, VoiceMode detectedMode, VoiceMode? confirmedMode)`.
- Core `WakeSession(WakeDetection initial, AppConfig config)`; `bool Confirm(WakeDetection candidate)` accepts only same-epoch longer-prefix evidence within 6,400 samples of initial onset; `WakeTranscript Resolve(string transcript)`; `bool HandlePress(VoiceMode mode)` indicates one accepted explicit stop for the currently detected/confirmed mode; `bool ConsumeRelease()` consumes that stop key's later release. One WakeSession belongs to exactly one App generation.

- [x] Add `WakePhraseChecks.Configuration`: fresh/legacy default Off, exact phrases and 2 seconds; explicit saved values survive roundtrip and equality; invalid/null phrases, identical normalized phrases, >80 characters, outside 2–6 display words and silence outside 1–10 rejected. Overlapping defaults must validate. Run to observe RED.
- [x] Add `Prefixes` assertions: `Hey, Sharp Voice dictate: hello there` resolves Dictation/`hello there`; `Hey SharpVoice, what is two plus two?` resolves AskAi/`what is two plus two?`; phrase alone and marker-only text become empty; omitted-prefix `hello there` remains intact; later `hey sharpvoice dictate` in a request body stays text; native longer confirmation picks Dictation with an omitted prefix. Preserve punctuation/casing of remaining request text.
- [x] Add `WakeSessionChecks.OverlapAndKeys`: short detection stays provisional, one compatible longer confirmation changes mode, stale epoch/late body detection cannot change mode; stop press fires once and subsequent release is consumed, different-mode shortcut cannot start/promote an operation. Include Ask phrase as longer prefix in custom configuration to prove the algorithm is not hard-coded to default direction. Observe RED, then implement the signatures above.
- [x] Add explicit migration property-presence handling for wake settings alongside existing source-generated config normalization, include all wake fields in custom Equals, and call validation. Run build and full checks GREEN. Record evidence in wake ledger.

### Task 3: Shared microphone and lossless handoff

**Files:** Audio WakeAudioBuffer/WasapiAudioRecorder.Wake/WasapiAudioRecorder and tests WakeCaptureChecks/MicrophoneActivationChecks.

**Interfaces:**
- Audio `public delegate void WakeAudioHandler(ReadOnlySpan<float> samples, long firstSample, long epoch)`.
- Recorder `void SetWakeListening(bool enabled, WakeAudioHandler? receiver)` changes independent capture demand and clears rings on disable; receiver must only enqueue owned raw samples.
- Recorder `Task BeginWakeAsync(long startSample, long epoch, CancellationToken ct = default)` atomically seeds gated recording from an 8-second ring with 3,200-sample leading padding and attaches live samples. Reject expired onset, discontinuity and stale epoch. Read-only `long CaptureEpoch` identifies each fresh native capture lifetime.
- The raw callback and gated handoff buffer use the same absolute sample cursor; rings never survive an epoch. Normal `BeginAsync` and 250 ms Keep Ready pre-roll stay unchanged.

- [x] Add fake-driver `WakeCaptureChecks.Demand`: wake Off+OnHotkey opens no mic, enabled wake uses exactly one session, disabling closes unless KeepReady/preview demands it, pause always closes and clears history, resume starts a new epoch. Run RED using the existing injected IAudioCaptureSession fixture pattern.
- [x] Add `Gapless`: feed numbered known audio through split blocks, detect inside a block, delay BeginWakeAsync while more arrives, then assert every sample from onset-minus-padding to final tail occurs exactly once. Test the eight-second boundary, overflow/expired onset, device replacement, cancellation during arming and independent draft/final buffer ownership.
- [x] Add `UngatedInput`: below-gate samples arrive intact at detector callback, while normal transcription buffers retain configured filtering/gating. Separate raw copy before AudioFilter.Process; do not change filtering for existing hotkey sessions. Keep callback nonblocking; never call sherpa directly there.
- [x] Implement demand and bounded rings under existing capture ownership, rejecting a concurrent Begin instead of replacing an active recording. Wake session max duration remains MaxRecordingSeconds including seeded audio, with max-stop signaled outside the recorder lock. Run full managed checks and capture fixture GREEN, preserving all existing OnHotkey/KeepReady tests.

### Task 4: Provisional activation and lifecycle integration

**Files:** App SharpVoiceApp.Wake plus main/Live/WritingTools/Answer/Workspace/Editor/Connected lifecycle integration; Core WakeSession refinements where required; tests WakeSessionChecks plus isolated App fixture runner.

**Interfaces:**
- App partial `InitializeWake(HttpClient http)`, `Task PrepareWakeAsync(CancellationToken ct)`, `WakeDetected(WakeDetection detection)`, `RefreshWakeListening()`, `Task StopWakeAsync()` own lifecycle. Keep these private to App; production and fixture use an injected detector/capture boundary rather than real devices.
- Core resolution/session interfaces from task 2 and recorder interfaces from task 3 are authoritative. Record `WakeSession?` beside existing operation generation, never in a global reusable transcript variable.

- [x] Add isolated state/coordinator fixtures for initial short match then Dictate suffix with Backend=none; assert one recording, Dictation final route, no Ask/LLM/conversation reservation. Add short Ask with no provider: helpful configuration failure after resolution, no insertion. Add selected-text snapshot delayed beyond initial detection and current-window change: original snapshot remains authoritative and unsafe insertion is blocked.
- [x] Start a provisional recording immediately at the first match using BeginWakeAsync, preserving audio before selection work. Snapshot target/settings once. Do not call existing Pressed(AskAi) directly: it rejects no-provider/Connected selection paths too early. Factor shared capture-start setup as needed, then resolve the mode after final transcript cleanup and before WritingTools.Prepare, provider checks, Smart Ask/Connected routing and conversation leases.
- [x] Continue detector only for a same-onset longer confirmation of the provisional session; do not permit another activation. Draft cleanup uses WakePhrases.Resolve to remove prefixes and show a tentative mode; final resolution is authoritative. Captured selection is used only for the resolved non-Connected path; Connected receives no selected/document text. Correctly dispose unused SelectionSnapshot ownership.
- [x] Make origin-specific shortcut handling consume release after explicit stop, preserving Hold/Toggle behavior for hotkey-owned sessions. Silence watch uses WakeSilenceSeconds regardless of RecordingGesture, starts from seeded audible history, and always respects max duration/explicit stop. No transcript/phrase-only result must not reserve history/context or call providers.
- [x] Add lifecycle fixtures: TTS, all editor/reply/workspace starts, cancel, error, max-stop, config/device replacement and shutdown suspend/drain the worker, release idle audio and reject delayed callbacks. Rearm only after Idle+SpeechInputGate allows capture; use a 750 ms guard after generated output cues/playback, then reset stream. Keep wake readiness distinct from microphoneReady/model readiness and authBusy. No background operation may reopen a paused microphone.
- [x] Run build/full managed checks plus isolated prerecorded capture through the actual App coordinator and detector. Assert provider/tool/insertion sinks receive the expected request or no call; no real clipboard/accounts/device use. Run AOT fixture after managed GREEN. Record evidence and observed limits.

### Task 5: Compact themed preferences and runtime status

**Files:** UI NativeDesktop.Wake/Settings/Help/Presentation/WritingTools command dispatch; App Wake preparation callbacks; tests WindowsHelpSmoke.Wake/WindowsSettingsSmoke and runner registration.

**Interfaces:**
- UI `void SetWakePreparation(WakePreparation state)` updates cached authoritative service status on the UI thread.
- UI `Action? WakePreparationRetryRequested` retries failed preparation without changing saved preferences.
- Wake configuration saves through existing ConfigChanged validation/transaction; do not create a second config file or bypass existing active-session guards.

- [x] Add hidden-desktop native smoke assertions for Audio's Wake Phrases entry, Off/On, both phrase edits, silence duration, clear always-listening notice, progress/cancel/retry and error state. Include enabled-but-unavailable state and missing-model first run; assert ordinary hotkeys/settings remain usable and status never claims listening before preparation succeeds. Use fresh noncolliding control IDs verified against existing help/commands.
- [x] Add the Audio entry near live-transcription controls using a compact button rather than another large block of always-visible explanatory text; open a themed Tools-style window with editable defaults, 1–10 second duration and on-demand help. Support dark/light, existing icons/hover effects, keyboard access and DPI sizing. Do not add tray actions beyond the established simplified menu.
- [x] Wire automatically started download on enabling/restart, visible progress and cancellation, Retry after failure, and tray tooltip/ready text. Show Listening provisionally, resolve mode as evidence arrives, and describe silence stopping. Disabling cancels preparation and clears wake demand; cancellation must not undo the saved user preference or falsely report Ready.
- [x] Run managed and full native settings checks, capture and inspect light/dark output and verify no clipped controls/help. Update help to explain the reserved Dictate suffix and continuous speech. Record UI evidence.

### Task 6: Verify and stage the complete standalone app

**Files:** Delivery docs, wake ledger, package outputs under artifacts; no extension/protocol changes.

- [x] Run complete managed checks, native UI and prerecorded detector/App capture fixture sequentially. Benchmark idle detector CPU/memory and both activation modes; record host/runtime and input fixture. Identify physical microphone accuracy, room noise/false triggers and audible feedback as manual acceptance, not an automated pass.
- [x] Request the execution skill's independent final review using spec/plan/ledger and the five Review Focus cases; fix material findings with regression evidence. Do not delegate implementation tasks.
- [x] Publish Native AOT tests; run complete AOT suite, native settings and detector/App fixture. Verify exact runtime/tokenizer/model pins and notices in the packaged executable; compare disabled-wake OnHotkey behavior against the existing no-idle-capture fixture.
- [x] After all test processes exit, publish the app, guarded-stage EXE/PDB and verify staged/portable SHA-256 equality plus atomic update.ready. Preserve current live process and exact existing extension package. Record commands, counts, measured resource cost and output paths in verification/ledger; add README and Windows smoke instructions for both default phrases, auto-download, local microphone use, pause and recovery.
- [x] Report completion only for demonstrated shipped behavior, with the executable path and remaining physical-device acceptance checks. Do not call a partial detector or model placeholder finished.

## Self-Review

Coverage: detector/model/bundling (1), configuration/text/overlap policy (2), capture/ownership (3), routing/lifecycle/approval preservation (4), settings/privacy/progress (5), verification/delivery (6). Each Review Focus has explicit tests in its owning task. Native/tokenizer selection must be proven first rather than deferred until UI integration. The shared-prefix amendment is intentional user direction and supersedes the earlier non-prefix validation proposal. Execution proceeds natively under the existing workspace ruling after plan review, without worktree/commit permission requests.

Completion evidence: see the wake ledger and docs/verification.md. All implementation/delivery steps are complete; physical-device acceptance remains explicitly manual.
