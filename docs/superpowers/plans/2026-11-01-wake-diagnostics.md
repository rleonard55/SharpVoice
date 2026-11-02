# Wake Diagnostics Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline in the current workspace. The user explicitly chose “Build with these defaults; skip further design questions.”

**Goal:** Deliver a simple standalone Windows wake-word test app that produces useful, reviewable diagnostic reports.

**Architecture:** Reuse the real WASAPI recorder, keyword spotters, speech-end detector and wake-session prefix handling. A separate native window controls a serialized diagnostic session; capture callbacks never write logs or touch UI. Opt-in counters expose capture continuity and detector backlog. A whitelist-based journal/export omits credentials, configuration contents and speech audio unless the user explicitly enables guided-attempt clips.

**Tech Stack:** Existing C#/.NET 10, native Win32 controls, sherpa-onnx 1.12.32, Native AOT.

**Spec:** User request and approved defaults in this conversation: standalone utility; easy navigation; guided phrase tests; live health/event log; exportable reports; optional audio clips off by default. No further design approval requested.

## Global Constraints

- Do not modify saved SharpVoice preferences, run AI requests, paste, or install hooks.
- Microphone opens only after Start; Stop and window close release it.
- Refuse live testing while SharpVoice is running; never terminate it automatically.
- Default reports contain no audio, transcripts, user paths, device IDs, prompts or credentials.
- Guided audio is explicitly opt-in, bounded to 20 seconds per attempt and a maximum of 20 clips; session logs are bounded and flushed to disk.
- Share actual production capture/detection implementations; do not invent a confidence score that the runtime does not expose.
- Stay in the existing workspace: repository has no initial commit and cannot supply a normal isolated worktree. Do not initialize history or commit unrelated untracked files.

## Review Focus

- Missed detections versus invalid expectations: label expected phrase, provisional prefix and final mode separately.
- Silent or disconnected microphone: distinguish audio arrival, level, native gaps and stalled detector.
- Failure mid-test: keep the journal readable/exportable and always permit Stop/Close.
- Privacy: whitelist export fields and files; explicit audio opt-in; no arbitrary directory zipping.
- Long/repeated sessions: bound logs, clips and callbacks; serialize lifecycle without locks across native callback boundaries.

### Task 1: Diagnostic counters and report contracts

- [x] Add regression tests for recorder gap/counter snapshots and detector overload/reset counters.
- [x] Add optional capture/detector snapshots, disabled by default for production instances.
- [x] Test journal limits, private-data exclusion, optional WAV bounds and explicit export allowlist.
- [x] Implement `DiagnosticJournal` with immediate append/flush, typed JSON metadata and ZIP export of known report files.

### Task 2: Guided session engine

- [x] Test guided Ask/Dictation prefix resolution, missed/noise cases and repeated rearming using synthetic capture/detections or real fixture WAVs.
- [x] Implement `DiagnosticSession` with Start/Stop, guided attempts, passive hits, auto-stop/rearm, timeout and health snapshots.
- [x] Keep all native lifecycle work off the UI and out of microphone/detection callbacks; surface failures without losing reports.

### Task 3: Standalone window and distribution

- [x] Create `src/SharpVoice.Diagnostics` with microphone/phrase controls, Start/Stop, guided tests, Mark Missed/Finish, live health, event log, optional clips and Export/Open Logs.
- [x] Add deterministic native UI smoke coverage with a fake controller on an isolated desktop; inspect a screenshot.
- [x] Add solution/build packaging and a launcher for `SharpVoice.Diagnostics.exe`; embed pinned runtime/licenses.
- [x] Run complete managed/AOT suites and actual native synthetic wake sessions. Obtain independent review, resolve important findings, publish the standalone utility and document how to run/export reports.

Completed: managed and AOT suites 155/155; native six-session repeat, continuous-speech timeout, report allowlist and actual published EXE startup/close verified. One independent whole-change review, one fix pass. See `docs/verification.md` for evidence and remaining physical-microphone acceptance.

No live microphone capture, provider requests, clipboard operations or restarts of the user's app are part of implementation verification.
