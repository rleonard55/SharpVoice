# Voice Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Ship a persistent VS Code conversation panel, app-session-only conversational context and bounded local live transcription.

**Architecture:** Desktop owns a typed in-memory conversation store and serializes voice/model operations. Native windows and a versioned authenticated editor transport present snapshots/events and submit explicit intents. Rolling local draft inference shares the current Whisper factory and yields to final transcription.

**Tech Stack:** .NET 10/C#14 Native AOT, native Windows UI/WASAPI, Whisper.net, TypeScript/VS Code Webview View.

**Spec:** `docs/superpowers/specs/2026-11-01-voice-workspace-design.md` (user approved).

## Global Constraints

- Windows/local trusted editor only; preserve authenticated same-user IPC and strict frame limits.
- Retain conversations in memory only until desktop exit; no extension/webview persistence. Ordinary dictation history stays separate.
- General Ask is shared across popup, panel and hotkeys; editor/account/tool scopes stay separate.
- Max20 conversations,40 retained turns/thread,1,048,576 UTF-16 units; protect active turns and visibly omit oversized context.
- Preserve fresh target checks, exact diff approval, one Undo, cancellation and actual tool receipts.
- Draft inference uses local selected Whisper model, one call at a time, copied bounded audio; never an LLM input or final result.
- No user app/process/profile/account changes for tests. Builds/restores sequential; use cached Windows packages/artifacts and stage guarded update.
- Work directly in authorized workspace: parent Documents git does not track this project; no git mutations/worktree/commits.

## Review Focus

- Repeated hotkeys after closing a popup must retain context while a desktop restart drops it (tasks1/2/3).
- Switching provider, attached file or editor workspace must not silently export old context (tasks1/2/3).
- A panel owns focus but must not invalidate source or permit stale edit application (task3).
- A slow/noncooperative preview must not reuse pooled audio, overwrite final text or permit parallel native Whisper access (task4).
- A hidden/disconnected view must stop active capture and never resurrect approvals/actions (tasks2/3/5).

### Task1: Bounded desktop conversation contracts

Files: create `src/SharpVoice.Core/Conversations.cs`, `tests/SharpVoice.Tests/ConversationChecks.cs`; modify `src/SharpVoice.Core/EditorProtocol.cs`, `tests/SharpVoice.Tests/Program.cs`, `src/SharpVoice.Refiner/AnswerConversation.cs`, `SmartAskRefiner.cs`, `UnifiedRefiner.cs`, `EditorRefiner.cs`.
Interfaces: `ConversationStore` owns `HostId`, New/Select/List/Get/Begin/Complete/Cancel/Delete/Clear/Retire; immutable `ConversationView`, `ConversationTurn`, `ConversationScope`, `ConversationLease`. Context construction takes a view and returns bounded structured turns. `SmartAskRefiner.GenerateAsync` accepts optional conversation context; editor inputs can include bounded historical conversational exchanges, current source remains authoritative.
- [x] Write/run RED tests for cross-request history, store restart, scope isolation, completed vs canceled turns, stale lease results, active eviction protection, first/latest retention and budget omission.
- [x] Implement store/contracts with source-generated JSON and no persistence dependency; create prompt adapters preserving exact per-turn roles and provenance.
- [x] Run whole managed suite; record output and changes in ledger.

### Task2: Native conversations and hotkey integration

Files: create `src/SharpVoice.App/SharpVoiceApp.Conversations.cs`; modify `SharpVoiceApp.cs`, `SharpVoiceApp.Answer.cs`, `SharpVoiceApp.Connected.cs`, `src/SharpVoice.UI/NativeDesktop.Answer.cs`, native help/presentation as needed; tests native answer smoke and provider checks.
Interfaces: desktop `ConversationStore conversations`; general-scope active thread with provider-identity binding; UI New/Recent/Select/Delete/Clear callbacks and conversation view renderer. Existing `AnswerContext` owns only current insertion target; completed thread text does not retain selection ownership.
- [x] RED provider/native tests: a new hotkey request uses prior exchanges, popup close only dismisses, recent selection cannot paste to an old target, active delete cancels, no Ask content enters saved history.
- [x] Route both Smart Ask answers and inserted Ask results through store; exclude Ask from history persistence and recapture destinations. Dismissal retains completed history; new/delete/identity changes invalidate active leases and pending callbacks.
- [x] Preserve voice/text reply and Edge/Windows speech gating, timeout and cancellation recovery. Wrap Connected outcomes without allowing action replay or cross-domain prompts.
- [x] Run managed suite and isolated native smoke checks.

### Task3: Editor transport and VS Code panel

Files: create `extensions/sharpvoice-vscode/src/panel.ts`, packaged `media` resources, model/host tests; modify extension.ts/model.ts/bridge.ts/package.json; modify Core EditorProtocol, Platform EditorBridge/EditorPlugins, App Editor partial plus new workspace handler; native fixtures where protocol version used.
Interfaces: protocol3 `EditorFrame` gains optional host/client/conversation/turn/draft/snapshot fields. Persistent authenticated connection gets server-issued client/host IDs; frame whitelist binds all subsequent intents. General Ask without attached context shares desktop thread; context Ask and code threads bind to editor client/workspace/document.
- [x] RED Node/core tests: snapshots/schema bounds, stalehost/client/action rejection, reconnect clears old UI, panel supports voice/text clarifications and does not serialize content to disk.
- [x] Implement bounded snapshot/event IPC and keep subscription alive between operations. On disconnect cancel unfinished editor work; retain only completed history. Current one-editor connection limit remains explicit.
- [x] Implement themed accessible Webview View using packaged CSS/JS, restrictive CSP and text-node rendering. Idle status opens view; buttons/composer route whitelist actions. Support generic Ask without file, explicit Include Current File, code start/stop, persistent questions and review/Apply/Cancel links.
- [x] Adjust source restoration/focus validation for owned panel/diff; capture current selection/context on each new turn and validate freshness before applying. Hide cancels active work; reopen resynchronizes store snapshot.
- [x] Add Open/Move actions with native Secondary Sidebar placement/fallback; do not overwrite global layout or other views.
- [x] Run typecheck/Node suite, managed suite and isolated real VS Code host fixtures; bump bundled VSIX matching IPC.

### Task4: Local live drafts and all voice adapters

Files: create `src/SharpVoice.Speech/LiveTranscription.cs`, `src/SharpVoice.App/SharpVoiceApp.Live.cs`; modify Audio recorder snapshot API, Core config/migration, App capture/finish/cancel paths, UI Audio/HUD/answer/Connected as needed, panel draft handling. Add `LiveTranscriptionChecks.cs` and recorder/HUD/native tests.
Interfaces: recorder `Snapshot(int maxSeconds)` returns owned copied `AudioRecording?`; live worker Start/StopAsync uses cancellation/generation/sequence and injected snapshot/transcribe callbacks. Config `LiveTranscription` defaulttrue; UI On(Local Draft)/Off(Waveform Only). Draft events never use retained preview/history paths.
- [x] RED tests: bounded copied audio survives source reuse, silence/insufficient audio ignored, slow callback never overlaps, cancellation drains worker and suppresses stale events, live-off avoids inference, UTF16-safe trailing draft and migration defaults.
- [x] Implement single adaptive worker with initial1s delay, max8–12s snapshots and no queue. Share Whisper gate/preload; stop preview before final inference, clear buffers on every path. Preview failure degrades to waveform.
- [x] Wire every recording start and stop/cancel/dispose path, including editor/Connected/popup. Set Finalizing Transcript and replace draft only with final full-audio output. Update pill tail/small waveform and panel/answer statuses.
- [x] Run managed/native tests and benchmark fixed local fixture audio with installed Tiny Q5 model; record actual cadence/finalization overhead and tune bounded defaults.

### Task5: Final review and delivery

Files: README, docs/verification.md, windows-smoke-test.md, bundled VSIX and staged artifacts.
- [x] Run complete managed suite/native UI, Node/type checks and isolated editor end-to-end fixtures with synthetic providers.
- [x] Request fresh whole-change read-only review per executing-plans; fix important findings with observed RED/GREEN regressions and rerun suite.
- [x] Publish AOT tests then run complete suite/native fixtures; publish matched app only after child-process tests finish; verify no warnings/errors and package contents.
- [x] Stage app/portable/symbols with hash/atomic ready marker; do not restart user's app. Document right-sidebar setup, memory-only context controls, live draft limitations and remaining physical acceptance checks.
