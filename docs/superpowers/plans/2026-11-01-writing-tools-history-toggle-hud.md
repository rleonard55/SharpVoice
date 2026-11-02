# Writing Tools, History, Toggle Recording and HUD Implementation Plan

> **For agentic workers:** Execute inline using superpowers:executing-plans, with one fresh final independent review. No intermediate approval pauses: user explicitly approved the five proposed features and HUD change.

**Goal:** Add personal dictionary, voice snippets, app profiles, recent dictations/retry and toggle recording; move subdued HUD metadata onto the right of the title row.
**Architecture:** Immutable configuration records hold user-owned dictionary/snippet/profile rules and new preferences. Pure Core helpers perform bounded literal text transforms, resolve exact process profiles and hold a bounded history journal. Native management windows expose editable lists and before/after history; App owns encrypted opt-in history persistence and isolated retry work. A pure gesture router extends the existing dual hotkey state machine; optional silence timing stops only toggle sessions.
**Tech Stack:** Existing C#/.NET10 Native AOT, Win32/D2D/DWrite, generated JSON, existing DPAPI package. No new runtime dependencies.
**Spec:** User-approved five-item feature list and attached HUD annotation in this conversation.

## Global Constraints
- Preserve defaults: None cleanup, Tiny Q5, hold gesture, on-hotkey mic, eight-word threshold. Explicit old preferences survive; new collections empty, history memory-only, silence timeout 0 (disabled).
- Dictionary: explicit spoken/preferred phrases, literal Unicode boundaries, longest match, nonrecursive replacements. Ordinary dictation only; selected-source/Ask inputs untouched. Hint Whisper with bounded preferred terms. Apply dictionary once to the final text (after AI, if enabled); use preferred-term hints during local Whisper decoding.
- Snippets: exact whole-utterance phrase ignoring casing, surrounding whitespace and terminal punctuation; explicit user trigger, no partial/embedded matches. Expand verbatim before AI/threshold, ordinary unselected dictation only. No LLM call for a matched snippet.
- Profiles: exact executable basename, case-insensitive, optional .exe; one per app. Override provider/model/mode/threshold/instructions, otherwise inherit global settings. Snapshot at key-down; no per-app mic/gesture changes. Profile hint shown in HUD details.
- History: newest 50 text-only entries; raw transcript + result/context/mode/app/model/status, no audio. Retry explicitly uses chosen provider/model, bypasses ordinary cleanup threshold and updates preview/history only (never inserts into a stale target). Copy explicit. Delete/clear supported. Disk saving opt-in, encrypted with current-user DPAPI, atomic writes; disabling removes disk history. Failures cannot prevent dictation/pasting; report persistence failures.
- Toggle applies to both shortcuts; keep nested Ask promotion, debounce/release lockout, cancellation, pause and maximum duration. Optional 0–30-second silence stop uses level relative to gate, operates only while toggle recording and opens no extra mic session. Reset latch on rejection/end/cancel.
- Native managers use descriptive labels, normal keyboard navigation, themed controls/hover/help; saving rules uses normal config persistence. Draft cancel doesn't alter configuration. Active dictation blocks rules/history retry changes.
- HUD: 520x96 pill, title left; model/status small/muted/right-aligned same top row; lower two-line spoken preview with more space; D2D and fallback agree, text bounded, waveform reserved.
- Never stop/start user's instance, modify real preferences, contact live AI, or download weights. Stage release, mark ready only after validation; guarded portable copy.
- No Git commits/staging: workspace is untracked under parent repo.

## Review Focus
- Boundary/casing/Unicode/overlapping replacements, ambiguous snippet triggers and expansion limits: pure tests task 1.
- Old/missing/null JSON arrays, invalid/duplicate profiles and IDs; inheritance preserving global config: config round-trip tests task 1.
- History retry cancellation/stale results, selected-context semantics, delete/clear vs completion and privacy toggle: journal, service and native tests task 3/4.
- Toggle nested promotion/repeats/injected input, rejection/cancel/reset, silence and capture readiness: pure routing/silence tests + existing mic tests task 2/4.
- Theme/layout/keyboard safety of manager windows and HUD header overlap: isolated native tests/renders task 5/6.

## Tasks
- [x] 1. Core records/validation, literal dictionary/snippet transforms and profile resolution; RED/GREEN tests and JSON round trips. Files Core/WritingTools.cs, AppConfig.cs; tests/WritingToolsChecks.cs. Interfaces: WritingTools.Resolve(AppConfig,string), Prepare(string,AppConfig,bool,bool) -> PreparedDictation(Text,IsSnippet), Correct(string,IReadOnlyList<DictionaryEntry>), VocabularyPrompt(...).
- [x] 2. Gesture router and silence policy; RED/GREEN nested/repeated/cancel tests. Files Platform.Windows/RecordingGestureRouter.cs, GlobalKeyboardHook.cs; Core/SilenceStopPolicy.cs. Configure hook optional toggle flag; ResetRecordingGesture() cancels latch without promoting canceled gesture.
- [x] 3. Bounded history journal and encrypted opt-in store/retry contracts; RED/GREEN journal/serialization/cancel checks. Core/DictationHistory.cs; Refiner/DictationHistoryStore.cs (reuse DPAPI dependency). Journal snapshots entries immutably; update by stable Guid, deleted entries never resurrect.
- [x] 4. Wire App recording snapshots, Whisper hints, snippet bypass/dictionary, history capture + isolated retry, silence lifecycle. Preserve all existing insertion/selection checks. Retry never calls TextInjector. Files App/SharpVoiceApp.WritingTools.cs + existing; Speech/WhisperEngine.cs; Refiner/UnifiedRefiner optional forceCleanup.
- [x] 5. Native General management buttons, rule editor and history manager; gesture/silence settings. Files UI/NativeDesktop.WritingTools.cs, NativeDesktop.History.cs, Settings/Choices/Help/Presentation. Use callbacks to save immutable configs and retry/delete/clear history; draft cancel discards. Isolated native CRUD/theme/keyboard fixtures.
- [x] 6. HUD layout in D2D/GDI with shared bounds, smaller right-aligned metadata and two-line preview; metadata uses effective profile/model. Inspect light/dark long-content renders.
- [x] 7. Full managed/native checks, one independent final source review; fix material issues with reproductions. Strict app/test AOT, docs, guarded delivery/hash and ready marker.

## Ledger
- Ruling: proceed inline with routine UI/storage choices under explicit user authorization, avoiding redundant approval rounds.
- Ruling: dictionary/snippet matching is ordinary dictation only; Ask AI/selected editing keep the user's instructions/source intact. Snippet content is inserted verbatim and never sent to AI.
- Ruling: history retries are review/copy actions, not automatic reinsertion; this avoids stale window/selection edits. Retry is an explicit AI request and bypasses word minimum.

- Task 1: missing-API RED build captured in writing-tools-red.log; 89/89 checks pass after implementation, including literal/Unicode boundary, snippet intent, duplicates and profile/legacy persistence. Gesture RED build confirms missing router/policy before implementation.

- Tasks 2/3: gesture/history missing-type RED builds captured; combined 93/93 checks pass, including encrypted temp-file round trip. Recording integration, native management windows and HUD layout are implemented; native CRUD/lifecycle and retry service validation are next.
- Ruling: cleanup word threshold counts the original transcript, before dictionary substitutions can merge/split words; explicit retry bypasses it. Saving an editor also commits its current form so users need not click Add before Save.

- Ruling: dictionary substitutions happen once on final ordinary text, rather than before AND after cleanup. Two passes could chain a→b→c even with a nonrecursive matcher. Whisper still receives local preferred-term hints. History keeps the raw transcript for comparison/retry.

- Managed suite reached 95/95. Explicit retry regression failed 94/95 with forceCleanup disabled, then passed after restoring it. Native editor/history CRUD, keyboard save, draft cancellation, duplicate validation, chosen retry model/cancellation and delete/clear passed on an isolated desktop. Initial unpumped light snapshots were blank; fixture now pumps queued paints and checks actual background pixels before accepting renders. Final rerun pending latest refinements.

- Final independent source review found a P2 startup history race and P3 toggle guidance mismatch, with no other material findings. History restoration now gates Ready and the serialized disk-write queue; an explicit startup Clear suppresses restoration and runs after the read. The deterministic blocked-load regression reproduces the old ordering (95/96, specific startup-ordering failure), then passes with the load barrier. Toggle Ask AI/selected-edit hints now say press again. Increased the managers' notice height after inspecting the profile help wrapping in light mode.

- Final validation: zero-warning managed build; 96/96 managed and AOT checks; six native Settings/help/tool groups, default/custom Apply round trips and HUD rendering pass. Strict app/test AOT publishes both exited 0 with no warnings/errors. Docs and smoke checklist updated; verified release staged and guarded portable delivery checked. Live hardware/provider/display acceptance remains manual.
