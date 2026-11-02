# SharpVoice panel, session conversations and live transcription

Status: approved by user for implementation. Date: 2026-11-01.

## Intent and agreed choices

The user wants a useful persistent interface beyond the VS Code status-bar button, follow-up requests that remember earlier requests and answers, and visible transcription while speaking. They chose the right-hand VS Code sidebar, conversation retention only in memory for the desktop app's lifetime, and live draft text in the floating pill. Voice reply is primary; typing remains available. Their example is requesting a recipe, then modifying the same recipe through a subsequent request without repeating it.

This design groups those requirements into one shared session/conversation service with native and editor presentation adapters. It is an incremental foundation for the broader plugin architecture discussed earlier. A general third-party plugin runtime, MCP server/client expansion, and arbitrary plugin UI are separate work; they are not implied by this delivery.

## Current implementation and problem

- The editor companion has status-bar/editor-title commands, notification-based clarification, and native VS Code diffs. `VoiceEditorController` owns editor state and uses authenticated version-2 named-pipe frames. The connection is disposed after each editor operation.
- The desktop app separately coordinates ordinary dictation, Smart Ask, editor work and Connected Ask. The answer popup supports voice/typed continuation, but a new global hotkey request hides it and clears that conversation.
- `AnswerConversation` bounds prompt history to the first exchange and five later exchanges. It is attached to the popup rather than to a durable-in-memory conversation identity.
- `WasapiAudioRecorder` owns a pooled recording until Stop; it exposes level feedback but no safe read-only snapshot. `WhisperEngine` serializes inference and model loading with a semaphore. Segment callbacks currently occur only after Stop.
- The pill already has a text preview area, but uses the beginning of the text and cannot currently distinguish live drafts from finalized text.

## Approaches

The discussion considered expanding notifications/status controls, using a docked conversation panel, or relying on a standalone native window. The user selected the docked panel beside their code, with the pill remaining useful outside VS Code. A shared service is preferred to independent editor/native histories because both controls must continue the same explicitly identified thread.

For memory, the user chose app-session-only retention rather than saved chat history. For live text, the accepted proposal is local rolling-window drafts followed by a full final transcription. Cloud streaming would add a new provider dependency and ongoing transmission of microphone audio. Repeated transcription of the entire growing recording would create unnecessary CPU work. Those are not selected.

## User experience

### VS Code panel

Provide one SharpVoice Webview View with a microphone icon and an Open SharpVoice command. Clicking the idle status-bar entry opens the panel; editor-title recording/edit commands remain usable. The panel offers Dictate Code, Edit Code and Ask AI, session status, recent conversations, New Conversation, Delete and Clear All Conversations. Show a short first-request-derived title; no extra model request is needed to name a thread.

A compact header shows connection state and the current provider/model. The conversation shows final requests, answers, clarification questions, canceled/failed outcomes and applied/rejected changes. A provisional live draft sits above the composer while recording and is replaced by the final transcript. Primary controls follow state: Speak/Stop Recording, Reply by Voice, Cancel, and a text composer/Send fallback. Questions remain visible until answered, canceled or superseded; notifications become optional attention cues, not the sole interaction.

Code results keep the existing full VS Code diff. The panel offers Review Changes, Apply and Cancel when appropriate, with the target file/language and context scope visible. Apply remains one explicit approval for the exact proposal. No write occurs from a voice acknowledgement or from merely reopening a conversation.

Closing/hiding the view stops active capture and cancels unfinished inference/review, while retaining completed exchanges. Reopening restores a snapshot from the desktop service. Merely losing keyboard focus from the source editor to this panel does not cancel a session. Switching to another application while recording/editing retains existing cancellation safeguards. Editor mutations and changed document versions invalidate a pending proposal.

### Right-hand placement

VS Code's documented extension API supports contributed views and user placement, but does not allow direct default contribution to the Secondary Sidebar. The implementation must not silently move unrelated views or change global workbench settings. Provide Open SharpVoice and an explicit placement action that moves only the SharpVoice container if the supported host command permits it. Otherwise provide the native Move View/Move To Secondary Sidebar affordance and concise first-open guidance. Remembered view placement belongs to VS Code; do not repeatedly reposition an already placed view. Validate the actual installed host before claiming automatic right-hand placement. The supported VS Code minimum must reflect APIs actually used.

References: [Sidebar guidance](https://code.visualstudio.com/api/ux-guidelines/sidebars), [Webview API](https://code.visualstudio.com/api/extension-guides/webview).

### Native Ask AI and hotkeys

Keep the active general Ask AI conversation across Ctrl+Alt+Shift requests, popup dismissal and reopening for the lifetime of SharpVoice. Reply by Voice/Type Reply and the next global Ask AI request use that thread. Show its title and New Conversation/Recent Conversations controls in the native answer interface. Closing the popup dismisses it and cancels active work; it no longer deletes the thread. Delete/Clear All explicitly remove retained conversation content.

Keep existing hotkey routing rules: with Smart Ask enabled, ordinary questions stay in the answer window and writing requests can still produce insertion content. Remember both kinds of completed Ask AI result so a follow-up can refer to the recipe even if its first version was inserted. In the VS Code panel, Ask AI answers stay in the panel; Copy or an explicit insertion action is required to enter an answer into an editor. Ordinary Ctrl+Alt transcription/cleanup does not automatically receive conversational history.

Conversation memory never grants permission to overwrite an earlier insertion. A new insertion takes a fresh validated destination/selection; if no valid destination exists, retain the answer for Copy. A reopened historical answer must not reuse an old selection or text-control ownership object.

### Live pill

Display Listening with a small Live Draft label, newest readable spoken text, and a smaller waveform. Target roughly 1–2 seconds between updates on suitable hardware using the default Tiny Q5 model; this is a target, not a guaranteed latency or literal word-by-word streaming. Recent draft words may revise. Use trailing text and Unicode-safe boundaries so long speech keeps visibly progressing.

After Stop, show Finalizing Transcript, perform the existing full-recording pass, and replace the draft with authoritative text. No preview text is pasted, committed to conversation history, or sent to the LLM. Empty/failed drafts must not turn into a no-speech decision; the final audio/transcription pipeline remains authoritative. Draft failures fall back to the waveform while final transcription stays available.

Apply live drafts to ordinary dictation, Ask AI, answer-popup replies, Connected Ask follow-ups and editor recordings. Display them in the panel for editor-originated work as well as in the pill. The native answer popup can show the draft/status during its voice replies without replacing the retained previous answer.

Add Audio → Live Transcription: On (Local Draft) / Off (Waveform Only), default On for new and migrated profiles. Off avoids preview inference; final transcription is unchanged. Keep concise contextual help rather than a persistent explanation block.

## Shared state and boundaries

### Desktop-owned conversation service

Introduce a UI-independent service with opaque conversation/turn identifiers, typed entries, bounded context construction and an event/snapshot API. Keep active operations separate from completed history. A conversation stores its domain, origin/scope, timestamps, user-facing title, completed turns and relevant context provenance. Track provisional, proposed, applied, canceled and failed states accurately; a generated proposal must never appear as an applied edit.

Use explicit domains: general Ask AI, editor-context Ask AI, editor code, and Connected Ask. The general conversation is app-wide and shared by native hotkeys, popup replies and panel Ask AI without document attachments, so changing a text destination or opening the panel does not reset a recipe thread. Explicitly including a document selects an editor-context thread with a visible scope badge. Editor-context conversations are scoped to the authenticated editor client/workspace and, for code operations, the document URI. Recent threads are selectable within their domain/scope. No automatic merging of code or connected account material into a general question. Changes to provider/account, editor workspace, or Connected Ask runtime/tool permissions retire incompatible active contexts; transcript entries can remain readable but cannot be silently sent to a different identity/provider. Starting a new conversation selects a new empty thread.

Desktop owns all conversation text. Editor views can reconnect and fetch snapshots from the same running desktop session. A desktop restart produces a new host-session identity; the extension must discard its old transcript/draft state and show a fresh session. Use no `globalState`, `workspaceState`, Webview `setState`, localStorage, serialized Webview state, transcript files or content logs for conversations. VS Code may retain placement preferences only. Completed conversations are excluded from existing persisted dictation history even if Persist History is enabled; ordinary dictation history remains governed by that separate preference. Existing previously saved history is not deleted by this change. This defines SharpVoice storage behavior; provider-side retention remains provider-owned.

Builder choices: initially retain up to 20 conversations, 40 completed turns per conversation, and a global text budget of 1,048,576 UTF-16 units (approximately 2 MiB of character storage). Never evict an active operation or a pending approval. Evict least-recently-used inactive threads when necessary. Within a thread retain its opening exchange and recent completed turns; show when earlier context was omitted. Construct each prompt within a separate bounded budget, retaining the opening request and recent exchanges and preferentially preserving the newest complete result. Do not generate model summaries in this milestone. Oversized individual context is explicitly clipped with omission metadata rather than silently presented as complete.

### Context and application safety

Store conversational text separately from ephemeral editor/source/tool context. Editor source is captured anew through the existing explicit context permission before each coding turn and validated again before Apply. Old conversational code/proposals are historical data, never current source truth. A file switch selects a document-specific code conversation or creates a new one; continuing discussion of another file requires selecting its thread. Generic panel Ask AI can run without an open editor, uses no source by default, and offers an explicit Include Current File control within current permission/size bounds. A selected snippet or fresh attachment is identified visibly and is scoped to that turn.

Connected Ask retains its existing selected-tool catalog, account/profile binding, source provenance, complete change previews, one-shot approvals and service receipts. Retaining a conversation must not resurrect an approval, replay a tool call, or reauthorize a prior action. Close cancels an unfinished action. Reopening shows the actual completed/canceled/unknown outcome and permits a fresh request with fresh checks. Shared conversation envelopes can wrap its existing tool-backed state without copying opaque tool payloads into unrelated threads.

Capture, inference and playback remain single-owner services. Both native and editor adapters issue the same typed start/stop/reply/cancel requests. Clarification continuation has one turn ID regardless of voice or typed input. Cancel/close/delete invalidates pending generations and late callbacks; cancellation completes native microphone/playback closure before another session is admitted. Timeouts in arming, recording and inference restore usable controls. Closing a view must not dispose services still owned by another view, nor allow a detached active microphone recording.

### Editor protocol and UI adapter

Extend and version the authenticated named-pipe protocol to support connection/session snapshots, conversation selection/lifecycle, typed/voice Ask AI requests, transcript drafts and turn outcomes. Keep compatibility checks explicit; bundle a matched desktop app and VSIX. Each event carries host session, editor client, conversation, turn and generation identifiers where applicable. Reject stale/mismatched IDs, excessive frames and unsupported actions. Bind editor client scope to the authenticated connection; a client-provided ID alone is not sufficient authority to subscribe to another editor's document context.

The current per-operation disconnect lifecycle must change so a panel can remain subscribed while idle. Transport loss cancels unfinished editor work but retains completed host-owned history; reconnect resynchronizes a bounded snapshot and never resumes an unapproved action. The existing one-active-editor transport limit may remain in this milestone with an explicit busy message for another editor window. General desktop Ask AI remains independently accessible while no editor connection owns an active operation; operations are serialized globally.

Webview sends a small whitelist of validated intents, never commands, arbitrary file paths or tool arguments. Use restrictive CSP, packaged resources, escaped/text-node transcript rendering and no raw model HTML/script execution. Read-only Markdown rendering is optional if it has equivalent escaping and no command links. Honor VS Code theme variables, high contrast, keyboard focus, accessible names, reduced motion and narrow layouts. Open panel without stealing the active source target; commands restore/freshly validate the remembered source before capture or changes. Editor focus checks must understand an owned panel/diff while retaining window/process/version checks.

### Local draft worker

Add a recorder API for a bounded copied snapshot of currently captured audio under its existing lock. Never expose the mutable pooled capture buffer or return it while a preview worker reads it. Keep snapshot copies short and limited (initially up to 8–12 seconds of recent 16-kHz audio). Silence/insufficient audio produces no draft. Clear/return snapshot buffers on success, cancellation, errors and disposal.

Run one adaptive worker per active recording. Wait for the selected model preload; model downloads remain the normal model path. Submit one local preview inference at a time; skip obsolete updates instead of queueing audio snapshots. Do not create a second resident Whisper model or run concurrent calls against the existing native factory. Prefer simple trailing-window text display over claiming a stable complete live transcript; final transcription always uses the original complete audio.

Stop/cancel the preview worker when recording ends, before final inference acquires Whisper. A noncooperative preview call must finish under its owned serialized worker before model/factory/buffers are released; suppress all its late UI callbacks. Bound windows and adaptive cadence to limit finalization delay. Benchmark an installed fixture/model to characterize this delay rather than assuming the native inference responds immediately to cancellation. Heavy models/slow hardware reduce cadence or suspend previews visibly; final inference takes priority over starting another preview. The audio callback never blocks on Whisper or on a UI event.

Drafts carry capture generation/turn identity and sequence. Receivers discard old or out-of-order drafts and clear drafts on Stop/New Conversation/Cancel; a late event must not overwrite a final response or the next recording. No draft audio/text is persisted or sent to remote services. Provide diagnostics with durations/counts and identifiers only, omitting user content.

## Delivery order and verification

1. Extract shared in-memory conversation/lifecycle contracts and cover retention, scoping, omission, deletion, restart and stale-message rejection.
2. Migrate native Ask AI/popup replies and hotkeys to conversation identities; preserve routing, fresh destination checks, speech gate and cancel/empty-input recovery.
3. Extend the editor bridge and add the panel, persistent clarification/composer, conversation snapshots and preview commands. Preserve explicit diff approval, one Undo, no auto-save/execution and fresh document validation.
4. Add bounded local draft capture/worker and common events, then connect pill/panel/native follow-up views. Include the Audio preference and legacy migration.
5. Verify end to end, package the matched VSIX/app, document placement and stage the update without closing/restarting user sessions.

Behavioral tests must first reproduce context loss between hotkey turns, restart forgetting, canceled/failed/unknown actions remaining unexecuted, stale draft overwrites and premature pool reuse. Synthetic provider tests inspect the actual bounded input and enforce domain/provider boundaries. Native UI fixtures exercise both themes, draft tail, controls, delete/reopen and voice/text fallback. Node and isolated real VS Code host fixtures exercise panel opening, right-side placement/fallback, focus-safe start/stop, persistent clarification, typed answers, diffs/Apply/Undo, source changes, disconnect/reconnect and desktop-session changes. Test that Webview model text cannot execute HTML/commands and that conversations never touch persistence APIs.

Run managed and Native AOT suites, extension type checking/tests, and AOT publications. Benchmark preview cadence and stop-to-final inference with synthetic/local audio on the installed hardware without using the user's microphone or live provider accounts. Report actual measurements, not guarantees for every model. Physical microphone, audible TTS, provider acceptance, accessibility and actual desktop layout remain manual acceptance checks. Never alter the user's live editor profiles/accounts or stop their apps to verify/package this work.

## Builder discretion and exclusions

Numeric retention/window/cadence limits above are proposed implementation defaults and may be tuned from measurements within the same bounded behavior. Exact component names, layout spacing, icon selection and transport payload representation belong to implementation planning. Keep the existing supported Windows/local-editor boundary; WSL, multi-file automatic edits and arbitrary plugin hosting remain out of scope. Do not add cloud audio transcription, paid Platform API keys, new providers or automatic external actions as part of this feature.

## Builder check

An independent read-only builder check returned “nothing important left.” Numeric bounds, placement fallback and scoped context behavior above are proposed implementation choices for design approval. No product code has been changed for this design.
