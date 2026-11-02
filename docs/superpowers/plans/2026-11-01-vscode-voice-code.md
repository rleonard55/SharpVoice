# VS Code Voice Coding Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with one fresh final review. The user approved the design and instructed execution; no additional plan-approval round. Work in this existing untracked workspace without Git commits/worktrees.

**Goal:** Deliver a Windows VS Code VSIX and SharpVoice update for document-aware voice insertion and previewed refactoring.
**Architecture:** Core bounded editor contracts and lifecycle; Refiner dedicated structured code generation through current transports; Windows current-user authenticated named pipe; App explicit recording controller; TypeScript local VS Code extension owns context, diff and edits.
**Tech Stack:** .NET10 Native AOT, existing provider clients/WASAPI/Whisper, Windows named pipes/ACL, Node extension host, TypeScript, VS Code API.
**Spec:** docs/superpowers/specs/2026-11-01-vscode-voice-code-design.md (approved by user “go ahead”).

## Global Constraints
- Existing global hotkeys/settings, ordinary paste and history stay intact. No editor source in saved history; no automatic installation, launch, live mic/provider/credentials, or Marketplace publication.
- Windows local file/untitled editor, one cursor/request; explicit toggle commands with user-assigned shortcuts. Context max32,000 UTF16, messages1MiB. Native source version checks and one Undo; every replacement/refactor previews. Unknown/partial output never inserts.
- Same-user pipe plus per-app token; 2-minute generation, five-minute proposal/clarification lifetime, max3 clarification rounds; no clipboard/shell/tools/filesystem authority in model responses.
- Stage app/VSIX only after checks; never stop/start user's instance. Remove ready marker before publication, recreate after validation.

## Review Focus
- Stale editor version/focus/cancellation vs completion/apply: extension session tests + Core state checks.
- Ambiguous targeting/intent and malformed generation: fake provider tests and extension pure tests.
- Pipe framing/auth/current-user discovery and disconnected previews: IPC isolated fixtures.
- Capture ready/cancel/max duration and global hotkeys concurrent editor sessions: app controller tests/pure state + existing checks.
- Native AOT JSON + extension local-only packaging: strict publish and VSIX manifest/content validation.

### Task 1: Core contracts and code refiner
Files Core/EditorProtocol.cs; Refiner/EditorRefiner.cs, UnifiedRefiner.cs; Tests/EditorChecks.cs.
Interfaces EditorContext, EditorRequest, EditorResult, EditorFrame, EditorDiscovery; generated EditorJsonContext; EditorValidation.ValidateContext/ParseResult. EditorRefiner.GenerateAsync(request, config, ct) returns EditorResult plus model callback, using optional dedicated prompt/input in UnifiedRefiner common transport.
- [x] Write failing bounds/result/prompt/short-request fake-provider checks; build to RED.
- [x] Implement strict bounded structured protocol and dedicated source-as-data code prompt. Never parse markdown; mode-specific insert/replace/clarify/unsupported, full fixed-range replacement only.
- [x] Run suite GREEN; record evidence.

### Task 2: Authenticated bridge
Files Platform.Windows/EditorBridge.cs; tests/EditorBridgeChecks.cs.
Interfaces EditorBridge(discoveryPath, Func<EditorFrame, CancellationToken,Task<EditorFrame>>, disconnect callback); Start; SendAsync(frame); Dispose; per connection request IDs. Discovery JSON contains protocol/pipe/token/PID; restrict file/directory DACL to current SID; pipe CurrentUserOnly. Newline UTF8 frame cap1MiB; handshake capability constant-time; no oversized/malformed clients.
- [x] Failing isolated temp-discovery/pipe auth/frame/disconnect/busy tests.
- [x] Implement bounded reader, serialized writes, one authenticated client; atomic discovery file; cancellation and cleanup.
- [x] Run isolated Windows checks GREEN.

### Task 3: App editor lifecycle
Files App/SharpVoiceApp.Editor.cs + lifecycle hooks; Core/EditorSession.cs tests.
Interfaces explicit editor start/stop/cancel/clarify/ack messages; session ID/context retained memory only; app bridge callbacks marshal commands onto desktop UI. Capture readiness before cues, reuse speech/preload/provider. Editor Refiner snapshot config profile for Code; no generic injector/history path. Stop/hardlimit route to editor, pause/global key guard, proposal/ack deadlines, disconnect cancels. success ding only matching applied ack.
- [x] Failing pure lifecycle tests for busy/stale/ack/timeout and explicit phases.
- [x] Implement bridge host and separate recording completion pipeline with existing recorder/Whisper/model APIs; UI state uses existing serialization.
- [x] Build and run existing+new tests GREEN.

### Task 4: VS Code extension
Files extensions/sharpvoice-vscode/package.json, tsconfig.json, src/{extension,bridge,model}.ts, tests, README/LICENSE/build script. Commands dictateCode/editCode/stop/cancel/apply/clarify. Context settings nearby/selection/document; version/focus/local-language/readonly/multicursor guards. Candidate selection-range provider then explicit quick pick confirmation, fallback select-code guidance. Native virtual diff; insert only zero-width code-entry, apply once and one Undo, clarification capture max3, stale/disconnect/close cleanup.
- [x] Failing Node model/transport tests for stale/cancel/context/result/default-preview decisions.
- [x] Implement extension; typecheck/build, run Node tests. Build local VSIX without installing/publishing; validate archive manifest/contributions/runtime files.
- [x] Run feasible VS Code integration tests on isolated test instance/desktop; document unavailable host/dependencies clearly.

### Task 5: Final validation/delivery
- [x] One independent fresh review of implementation/spec; fix material findings with RED/GREEN, no redundant re-review.
- [x] Full managed suite and native Settings/HUD checks; extension build/tests/package; strict app/test AOT and AOT suite/native checks.
- [x] Update README/verification/smoke checklist/plan ledger. Guarded portable copy, matching hashes/PDB, ready marker and packaged VSIX. Report setup and practical manual-test limits.

## Ledger
- Ruling: inline execution under existing approved design; preserve workspace/no Git mutations. Plan review is internal, not an extra user approval barrier.
- Preflight: task1 contracts shared by bridge/app/extension; all use newline JSON snake_case, version1, fixed target range, stable request ID. Task2 callback marshalling owns UI mutation; task3 provider remains app-only; task4 never accesses provider credentials.

- Task1 initial RED build failed on missing editor contracts, then 99/99 checks passed after contracts/session/pipe implementation. Structured fake-provider generation added, reaching 100/100; confirms code requests bypass ordinary word threshold and preserve whitespace/model/context. Extension missing-module RED captured, then three Node behavior groups and TypeScript typecheck passed. Task2 authentication mutation regression in progress.
- Ruling: editor commands start explicit toggle sessions without changing saved global gesture. Windows local documents are supported first. Existing Code app profile resolves provider before opening mic. Generated proposals never enter generic history/injector. Document size capped at two million characters for bounded full-document diff snapshots; sent source remains ≤32,000.
- Task3 cancellation must reset the shared App phase even when the editor work is canceled; added editor-finalization callback to mirror the generic worker's cleanup. App-global release events do not stop editor sessions; only explicit stop or recorder maximum/silence does.
- Task4 native extension-host tests run with synthetic text/replies and an isolated desktop/profile; focused-window predicate is injected only in test controller because offscreen desktops cannot have physical input focus. Production controller always uses real VS Code window state.
- Authentication RED mutation deliberately accepted a wrong capability: isolated suite failed specifically on the pipe authentication check (99/100). Restored constant-time capability verification before final builds.
- Final single review found four P2s: provider input accidentally included document URI; partial replace JSON could default to deletion; current editor identity alone does not prove keyboard focus (public VS Code API limitation); pipe construction outside try could fault accept after four stalled clients. Regression/fix pass in progress. Asked user to choose preview-for-code-entry versus explicit focus restoration before immediate insertion; refactors always preview. Independent installed-editor test was blocked by its updater mutex; an official standalone ZIP host is used instead, without touching the updater.
- Nearby-context regression reproduced an oversized window in a deep multiline buffer; corrected window anchoring and confirmed four Node groups pass.

- User ruling: “Preview code entry too (Recommended).” All inserts/replacements now require native diff and explicit Apply. Start explicitly presents the selected source editor; Apply returns to the source and revalidates before editing. No claim of detecting internal terminal/sidebar keyboard-focus changes through activeTextEditor. Design and extension help updated.
- Final-review RED: 98/101, exactly three failures for malformed partial replace output, absolute URI in provider payload, and pipe exhaustion recovery. Fixes require every result field explicitly, whitelist provider context without URI/version/range metadata, and move pipe construction inside the retry boundary. Native host also reproduced applying through a hidden stale TextEditor reference leaving source unchanged; Apply now obtains the visible source editor and rechecks the snapshot.
- Native standalone VS Code fixture GREEN: code-entry diff/Apply/native Undo; refactor diff/Apply/native Undo; source changes cancel and stale result ignored; explicit Cancel. The old hidden-TextEditor Apply behavior failed first; focusing/reacquiring the source editor fixed it. Installed editor/updater remained untouched. No live microphone/account/clipboard was used.
- Post-review suite initially reached 100/101 because the formerly valid insertion fixture omitted the now-required empty question field. Corrected the fixture to a complete three-field envelope; invalid missing-field regressions remain intact. Final rerun pending. App AOT publication exited 0; test AOT publication in progress.

- Final: zero-warning/error managed build; 101/101 managed and AOT checks; 4 Node groups/typecheck; all native Settings/HUD checks; 4 managed and AOT real VS Code fixture groups pass. Final VSIX installed in isolated test profile successfully and archive/runtime validated. Strict app/test AOT clean exit0; setup/verification/smoke docs updated. Guarded portable copy/hash and VSIX delivery completed. No real app/editor profile/microphone/account/clipboard changed. Physical/live quality/accessibility/DPI checks remain manual.
