# Microphone Activation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with a final independent source review.

**Goal:** Offer On Hotkey (default) and Keep Ready, with an explicit temporary Audio preview.
**Architecture:** Persist microphone_activation IDs on_hotkey/keep_ready. Separate native capture sessions from recorder orchestration so idle On Hotkey owns no audio device, and lifecycle tests use fake drivers. A short Arming phase waits for capture readiness before the start cue; release/cancel during startup discards the attempt. Preview has no transcription and ends on navigation/close/pause/recording.
**Tech Stack:** C# .NET10, event-driven WASAPI, native Win32 Settings, Native AOT.
**Spec:** User-approved two activation modes; On Hotkey default. Existing 250 ms pre-roll belongs only to Keep Ready. Recording retains its existing 50 ms release tail.

## Global Constraints
- New and legacy config defaults to on_hotkey; preserve explicit keep_ready.
- No native capture device opened while idle On Hotkey; release resources after completion/cancel/preview stop/pause.
- Keep Ready retains fast starts/local pre-roll; pause overrides both modes.
- No microphone activation merely from showing Settings. Explicit preview is local, temporary and independent of dictation.
- Start cue occurs only after native Start succeeds. Release during arming cancels without transcription/insertion or a false start cue.
- Preserve model/provider defaults, selection guards, Ask AI promotion and existing sound semantics.
- No actual user configuration, running-app interruption, live inference or OAuth changes.

## Review Focus
- Cancel, pause or release during a slow device open cannot leave capture running or create a stale start cue.
- On Hotkey completion must close native resources before returning to idle; Keep Ready stops only on pause/dispose.
- Failed preview/device opening and changed settings must not strand resources or prevent retry via Apply.
- UI preview stops on tab navigation/close/apply/activation and never implicitly starts from a level read.
- Worker disposal, partial construction and pending tasks must terminate safely.

### Task 1: Capture lifecycle
- [x] Extract the native WASAPI session behind an internal injectable driver seam.
- [x] Add fake-driver checks for cold idle, ready acknowledgement, end/cancel/pause, preview cleanup, open cancellation/failure and Keep Ready pre-roll.
- [x] Implement asynchronous recording start and demand-driven session ownership; run managed lifecycle checks.

### Task 2: Config and Settings
- [x] Add validated/persisted activation IDs, legacy default and explicit-mode round trips.
- [x] Friendly Audio choices plus explicit Start/Stop Preview; stop preview on navigation/close/apply/recording/pause.
- [x] Native Settings checks for modes/preservation/preview callbacks and light/dark visual checks.

### Task 3: App and delivery
- [x] Integrate activation policy, arming/early release/promotion/cancel and only-ready start cue.
- [x] Managed suite, Settings checks and independent source review; resolve material findings.
- [x] Strict native app/test publication and native suite/Settings; document hardware/manual limits.
- [x] Updated docs, verified staged update; preserve running portable copy if locked.

## Ledger
- No Git staging/commits: existing workspace is untracked under a parent repository.
- Source specification is the explicit two-mode proposal approved in conversation. Work proceeds without another approval round.

- Initial RED: missing activation setting, 71/72. First lifecycle green: 75/75 and default/custom Settings. Independent review identified native-close ordering and Keep Ready startup compatibility gaps. Regressions reproduced premature completion (74/75) and premature Keep Ready readiness (75/76). Per-session closure acknowledgements now gate EndAsync/cancel idle transitions; Keep Ready startup again acknowledges native Start/errors. Expanded blocked-disposal/cancellation and startup checks pass; managed suite 76/76. UI previews inspected in both themes.
- Cancellation keeps the app in Cancelling until native release and preserves later error/account/pause messages; cancelled startup never emits a start cue. On Hotkey has no pre-roll; Keep Ready retains 250 ms and both modes retain the 50 ms release tail.

- Final: managed/native suites 76/76; native Settings default/custom and explicit-device smoke pass in both builds. Native probe briefly captured in memory without saving audio or invoking inference. Strict app/test AOT and managed builds clean. README/smoke/verification updated. Verified staged executable marked ready; the running portable copy is preserved. Physical app hotkey/indicator/device-unplug checks remain manual acceptance.
