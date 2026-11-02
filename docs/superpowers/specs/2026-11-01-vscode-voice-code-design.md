> 2026-11-01 user correction: highlighting is optional with the default permitted document context. Protocol2 / plugin0.1.2 uses an automatically bounded document/nearby preview target plus original cursor when no selection exists. Explicit highlights, including new ones before clarification or during recording, refresh the target in the unchanged original document before generation. Selection Only remains restrictive. The older manual enclosing-range-picker requirement below is superseded; preview and explicit Apply remain mandatory. See `../plans/2026-11-01-editor-target-refresh.md` for repair evidence.

# VS Code Voice Coding and Refactoring

Date: 2026-11-01
Status: Approved for implementation by the user (“go ahead”). Amended by user: code entry also always previews, rather than restoring focus for immediate insertion.
Builder check: one independent read-only review completed; nothing important left to clarify before planning.

## Intent and agreed behavior

Add a VS Code companion extension so SharpVoice can enter and edit code using the actual language and unsaved document context. Keep speech capture, Whisper and the user's AI provider/account in SharpVoice. The user selected VS Code first, requested conversational refactors (try/catch, if, switch), and initially accepted previewing refactors and immediate insertion; the user subsequently chose preview for code entry too after discussing VS Code keyboard-focus limitations. Ambiguous requests ask a concise clarification. The selection is the first target; otherwise use the statement/block near the cursor when a reliable candidate is available. Larger project-wide operations are deferred.

Examples:

- “Insert a property called customer ID”: insert language-appropriate code at the captured cursor.
- “Wrap that in a try-catch”: replace the selected block through a diff preview. Ask about exception handling if the request/context does not establish a reasonable behavior; do not silently swallow errors.
- “Only run that if the customer exists”: propose a conditional around the target using identifiers from context.
- “Convert that to a switch statement”: preview a conversion; if the source cannot be converted without unclear behavior, clarify instead.
- “Make that an if statement”: ask for the condition when missing.
- “Extract that into a method”: preview edits within the current document only. Ask for missing intent when necessary.
- A rename in this milestone is limited to an explicit selected/local scope and previewed; project-wide semantic rename is deferred.

“Language-aware” means supplying actual editor language, source and formatting conventions to generation, using available editor language services for candidate targeting and validation. It does not promise that LLM output compiles or preserves semantics automatically.

## Approaches considered and chosen

1. Standalone Windows UI Automation plus manually chosen language: reuses today's insertion path but cannot reliably read unsaved buffers, exact ranges or editor diagnostics. Keep as existing generic fallback.
2. VS Code companion extension and local SharpVoice bridge: chosen in the conversation. Provides live document context, range-based edits, native diff/undo and explicit stale-document protection; requires an extension and app update.
3. Full independent coding agent/project indexer: deferred. Broad repository changes, terminal execution and a second account/agent stack are outside the requested first-document workflow.

## First milestone scope

Windows native VS Code desktop, local `file` and `untitled` documents, one active document and one request at a time. The extension runs locally (`extensionKind: ui`) and uses Node's Windows named-pipe client; web VS Code, SSH/containers/WSL remote documents, Cursor/other editors and multiple files are not supported initially. Local unsaved buffers are supported; source always comes from the editor, not from disk. Clarify setup limitations in extension help.

Preserve current app behavior outside an explicit editor session: hold/toggle preferences, On Hotkey microphone activation, Ctrl+Alt dictation, Ctrl+Alt+Shift Ask AI, tray actions, app profiles and normal paste protection continue to work. No automatic interception based only on a Code.exe process name or a lingering activeTextEditor value.

## Starting and finishing an editor session (proposed implementation choice)

The extension exposes **SharpVoice: Dictate Code**, **SharpVoice: Edit Code by Voice**, **SharpVoice: Stop Recording**, and **SharpVoice: Cancel** commands. Command/keybinding entry points require a focused window and an eligible active text editor; bindings use `editorTextFocus` and reject readonly/unsupported editors. No new default shortcut overrides an existing VS Code or SharpVoice shortcut in the first milestone. Users can assign keys in VS Code's normal Keyboard Shortcuts UI.

Starting either editor command starts one explicit toggle-style editor recording session; invoking the same command again or Stop Recording ends it. This does not change the app's saved global Hold/Toggle preference. The HUD shows Listening and a press-again/stop instruction. Escape/Cancel, pause, app exit and recording limit cancel/finish through the existing app lifecycle as appropriate. A small editor status item identifies recording/working/pending review and offers Stop/Cancel. Existing global shortcuts are rejected with a useful status while an editor session is active; they must not start a second recording or route its result through generic paste. Losing the original editor/window during recording or generation cancels the editor operation.

This explicit command approach avoids guessing whether VS Code's terminal, search box or source editor currently has keyboard focus. Context-aware reuse of global shortcuts may be a later feature.

## Context capture and targeting

At session start capture document URI, document version, language ID, text/cursor/selection, EOL/indentation settings and original editor/window identity. Only a single selection/cursor is supported initially; multiple selections ask the user to choose one. Check that the snapshot remains current before any provider call and before applying an edit.

Default context is the selected target plus bounded nearby source (maximum 32,000 UTF-16 code units total), relevant imports if they fit, symbol information if supplied by installed language services, and diagnostics touching the target. Truncate context at whole-line/code-point boundaries and label omissions. Do not truncate the edit target silently: ask for a smaller selection if it exceeds the limit. Context settings offer **Selection Only**, **Nearby Code (Default)** and **Current Document (Within Limit)**. No repository indexing or reading additional files in this milestone. Do not send the absolute filesystem path; basename, language and the necessary code are sufficient.

Dictate Code targets a zero-width insertion at the cursor, or explicitly replaces a captured selection. Edit Code by Voice prefers the exact selection. Without a selection, query VS Code selection-range/document-symbol providers for enclosing candidates. These providers vary by language and do not universally expose AST statement/block kinds: do not claim a generic nearest-block parser. Offer/highlight a provider candidate for the user to confirm, or ask them to select code when no dependable candidate is available. Blank lines, several possible targets, or missing providers must not silently expand the operation to the whole file. Target confirmation precedes recording or starts it only after confirmation, so “that” has a fixed meaning before speech.

For extraction, the fixed edit scope may be an explicitly confirmed enclosing function/class region in the same document so both call site and new method fit. If more scope is needed, cancel the proposal and ask for a broader selection; the model cannot change arbitrary ranges or other files.

## Request and provider behavior

Add a dedicated editor request/result contract and code prompt to the existing refiner layer; ordinary cleanup/answer prompts are not sufficient. Reuse all existing provider dispatch/account/model selection and app-profile resolution. The extension never receives provider credentials or invokes provider CLIs itself. Editor code generation/refactoring is an explicit AI request, bypasses the ordinary eight-word cleanup minimum, and does not apply prose dictionary/snippet substitutions. Backend None gives actionable AI & Models guidance, with no fallback insertion of spoken commands as code. Small local models may struggle; expose actual model and errors without guaranteeing equivalent quality.

Treat source, comments and diagnostics as untrusted context data. The spoken instruction is separate from that data. Return a bounded structured result: `insert` / `replace` with complete replacement text for the pre-agreed range, `clarify` with a short question, or `unsupported` with a reason. Never infer edits from code fences or prose and never apply a malformed/partial response. The model cannot choose filesystem paths, run tools or expand the edit scope. Plain code text inside the validated envelope preserves whitespace; no global trimming that changes code meaning.

Dictate Code also previews every result, including an insertion at the captured empty cursor. Replacing a selection, edit/refactor requests, uncertain intent and any expansion of scope always preview. If a Dictate Code instruction asks to alter existing code, return a preview proposal or clarification rather than inserting the instruction or a second copy of the surrounding function. Ambiguous intent defaults to preview/clarification. Unsupported structured output fails visibly and retains the transcript; never paste raw JSON or partial generated text. Check syntax/diagnostics where available, but successful text generation is not a compilation guarantee.

## Preview, clarification and apply

Use VS Code's native diff editor with a readonly virtual proposed document and the captured original snapshot, plus commands **Apply Voice Edit**, **Cancel Voice Edit** and **Clarify Voice Edit**. Keep the live document unchanged until Apply for every refactor. Clearly identify language, target and resolved model without permanent explanatory paragraphs. Do not implement Auto-Apply Simple Refactors in this milestone; the default stays preview and the optional setting can be introduced later with defined eligibility.

Clarifications appear as short editor notifications/input prompts, with an option to answer by a new explicit voice recording. Preserve the original instruction/context for the clarification round. Limit rounds and lifetime (proposed: three rounds, five minutes); otherwise cancel cleanly with the transcript recoverable. No clarification text goes into the source document.

Before preview and before Apply, require the same eligible document/editor and exact document version/range/source as captured, plus no cancelled/superseded session. Apply through a single native TextEditor.edit transaction with undo stops; no clipboard or simulated typing. Recheck without an asynchronous gap immediately before constructing the edit; rely on native edit rejection if the document changes during submission. Whole-document version changes, including unrelated typing and Undo, invalidate the proposal in this first version; ask to retry rather than attempt a merge. Apply is idempotent per request ID. No implicit save, formatting that expands the edit, build or terminal execution. One Undo restores the prior document content.

## Local bridge and lifecycle

Use a versioned, bounded message protocol over a Windows named pipe restricted to the current OS user. Local extensions authenticate with a per-session capability published in a current-user-only app-owned connection file; rotate it at app startup, never include credentials/source in discovery metadata, reject invalid handshakes and protocol versions. The capability protects against accidental clients, not malicious code already running as the same user. Validate envelope sizes, IDs, enum values, ranges, UTF-16 offsets, deadlines and message sequence. Proposed per-frame cap: 1 MiB; code context remains 32,000 units. Do not expose a TCP listener or workspace-controlled pipe/path discovery.

Handshake reports app/extension/protocol versions and availability. All request state is keyed by session and editor identity. Limit to one active recording/generation/clarification/preview operation across clients, report busy rather than queueing surprise recordings. Time out generation using the existing provider deadline with an explicit editor upper bound. Disconnect, VS Code window closure, cancel, pause and app exit cancel outstanding work and remove pending proposals; late frames are ignored. Closing the diff counts as cancellation, and explicit Apply/Cancel cleans up virtual documents/state. Provider generation completion does not mark a proposal Applied; the editor must acknowledge successful native insertion/apply.

The app uses the existing capture-ready/start tone, stop tone and warning behavior. Play the success ding only after an editor success acknowledgement; preview/clarification completion is not an insertion. HUD states distinguish Listening, Thinking, Review in VS Code, Needs Clarification, Applied and Cancelled. Generic TextInjector is never invoked for an editor session, including on bridge failure. Do not retain full surrounding source or proposed code in normal logs. First milestone editor source/context is session-memory only and excluded from saved dictation history; recover the spoken instruction through the current preview, and discard source/proposal on completion/cancel. More complete editor history is deferred.

## Components and packaging

- `extensions/sharpvoice-vscode`: TypeScript extension, manifest/commands/settings, focused-editor adapter, context/target capture, named-pipe client, readonly virtual document/diff workflow and transaction guard.
- Core: provider-neutral editor request/result records, validation, context limits and lifecycle states; no VS Code dependency.
- App: authenticated local bridge hosted for the user's process, explicit editor recording session, integration into existing serialization/cancellation/HUD/cues. Global voice workflow remains separate.
- Refiner: dedicated bounded structured code requests through the existing backend transport, preserving provider-specific cancellation/model reporting. Ordinary dictation/history APIs remain compatible.
- Release: app AOT update plus locally installable VSIX, documented install/pairing/setup and keyboard commands. Build reproducible VSIX without publishing to Marketplace or installing it into the user's VS Code automatically. Windows app contains no Node dependency; only the extension uses VS Code's extension host.

## Acceptance and validation

Pure and fake-provider tests cover contract parsing/bounds, empty/partial/malformed output, source-as-data prompts, clarification and unsupported responses, small-word AI bypass, original-range enforcement and cancellation/stale IDs. IPC tests cover same-user pipe/discovery restrictions, capability rotation/version mismatch, size bounds, competing clients, reconnect and shutdown; no live credentials or provider calls.

Extension tests use fixture buffers for C#, TypeScript/JavaScript and Python: language/unsaved text/indentation, exact selection, absent/ambiguous range providers, try/catch/conditional/switch requests, clarification, insertion versus replacement, native diff review and one-step Undo. Test unsupported/readonly documents, multicursor, changed version during generation and review, changed focus, closed diff/editor, duplicate Apply, malformed response and failure to apply. Use mocked generation for repeatability; diagnostics may depend on installed language services and are optional input, not proof of semantic correctness.

App regressions retain existing dictation/Ask/selection, hold/toggle/nested shortcuts, capture readiness/closure, chimes, memory-only history and native Settings/HUD behavior. Validate native AOT, build/typecheck extension, run feasible extension integration tests on an isolated test instance/desktop without altering the user's actual extensions/config or microphone. Document remaining physical editor/microphone/provider tests separately. Stage a reviewed app and VSIX; never close, replace or relaunch the user's running app automatically.

## Builder choices versus open user decisions

Proposed builder choices in this document: explicit extension commands and toggle-style editor sessions with user-assigned bindings; Windows local-documents first; range-provider confirmation/fallback selection rather than an invented AST parser; bounded nearby context by default; authenticated same-user pipe; no full editor source in saved history; native diff/Undo and conservative stale-version rejection. These were not independently chosen by the user and are presented for approval together with the design.

No further user preference is required to implement this bounded milestone if the design is approved. Global shortcut interception, auto-apply refactors, project-wide changes, remote workspaces and standalone AST/semantic tooling remain later work.

References: VS Code API (TextDocument, selection/symbol providers, TextEditor.edit, virtual documents and diff commands): https://code.visualstudio.com/api/references/vscode-api ; capabilities and exact method signatures must be checked against the chosen extension engine version during implementation.

## Approved focus-policy amendment

The user selected “Preview code entry too (Recommended)” during implementation. All code entry and refactors require a native diff and explicit Apply. Public VS Code APIs do not reliably distinguish an editor retaining activeTextEditor from keyboard focus inside its terminal/sidebar. Actual document/editor/window changes still cancel; no automatic insertion can occur from retained editor identity. Explicit Apply returns to the original source editor, rechecks the captured version/range without an asynchronous gap before edit submission, and creates one undoable change. Start-command enablement rejects readonly editors; commands operate on the explicitly chosen source.
