# Connected Ask AI — Design

Status: initial implementation complete, verified with synthetic tools and staged for review against the accepted visual-review amendment. See [Connected Ask setup and compatibility](../../connected-ask.md) for the implemented runtime boundary and supported actions.

## Intent

The user wants an optional way to ask SharpVoice about recent email or other connected information, and to perform actions using already-configured Codex tools. Reuse existing connections where the selected Codex runtime exposes them. Keep the existing subscription-based path; no paid Platform API backend or duplicate email OAuth implementation.

## Proposed Experience

Add Connected Ask AI preferences beside Smart Ask AI, off by default. Discover installed Codex runtimes and show the selected runtime/profile, sign-in/compatibility status and available integrations. Let the user explicitly choose integrations SharpVoice may access. This is a SharpVoice allowlist, not a modification to their general Codex settings. Distinguish connected/callable apps from directory entries that merely offer installation. Discovery and Test Connection do not read email content or perform actions.

When enabled, Ctrl+Alt+Shift starts a connected-assistant request. A branded SharpVoice popup shows activity (for example Searching Gmail), the integration in use, final answer and source links where returned. The compact response pill may summarize the result, but it cannot replace the connected-request activity/review popup. Keep existing Stop/Replay/Copy/Insert/Close actions where appropriate. Ordinary dictation, selected-text refinement and explicit VS Code code previews retain their existing paths. Connected requests use the selected Codex runtime; they do not silently fall back to another provider that lacks tools.

Examples:
- “What did Alex say in his latest email about the invoice?” Retrieve matching mail, clarify ambiguous senders/threads, then answer with source attribution. Do not fabricate a successful search if the integration is unavailable.
- “Reply that Tuesday works.” Resolve the specific original message and recipients; show the source email separately and a creation diff for the exact outgoing message, including recipients; wait for an on-screen confirmation before executing the tool.
- “Create a calendar event from that email.” Show a creation diff with the destination calendar, title, date, time zone, attendees and description before the write; require on-screen confirmation.

Read-only retrieval from explicitly selected integrations is allowed after opt-in. Side-effecting or unclassified tools pause for user confirmation. No speech synthesis or email content can approve an action. Confirmation is always a deliberate action on the visible review popup; voice-only approval is unsupported. No automatic send, delete, purchase or external write. Unknown/unrenderable approval requests fail closed.

## Mandatory Branded Activity and Change Review

This section records the user's amendment: connected operations must never be an invisible voice-only interaction. Every connected request has a visible SharpVoice popup showing what is being retrieved, done or proposed, which integration is involved, and whether work is in progress, awaiting review, completed, failed or uncertain. Read-only requests can run after integration opt-in and show their activity/results; they do not need a change-approval prompt.

Use SharpVoice's microphone branding, existing typography/accent colors and OS light/dark theme. Display a readable task summary and integration identity. Activity and source links remain available after completion; transient speech/HUD text alone is insufficient. Avoid raw tool JSON as the primary user interface. Keep Cancel/Stop visible while work is pending.

Every proposed external change opens a diff-style review in this popup **before the tool executes**:

- Text edits/replies: readable before/after panes or an inline diff, with additions/removals marked by labels as well as color. Preserve full text and provide scrolling; do not hide or silently truncate consequential content.
- New email/reply: show the original received email as separate source context. The change is a new outgoing message, not a modification to that received email. Show an empty/not-sent before state and the complete outgoing message after state, including To, CC/BCC, subject and attachment list.
- Structured records/calendar changes: a field-by-field before/after diff with destination, changed fields, additions and removals. Show unambiguous dates/time zones and affected recipients/attendees where relevant.
- Create/delete operations: show empty → proposed record or existing record → deleted explicitly. Use a specific confirmation label such as Send Reply, Create Event, Apply Changes or Delete Event.

Build the review from the actual final tool arguments and authoritative pre-change data, not a model-authored paraphrase. The target account/record and relevant unchanged identity fields must remain visible. For existing records, obtain a fresh before state and use version/conditional-update checks where supported. A changed source or changed arguments invalidates the existing review and requires a new diff and confirmation. Do not fabricate a before state; if the runtime cannot expose enough information to render an accurate review, refuse that change.

No change executes until the user explicitly activates its on-screen confirmation control. A spoken “yes,” a narrated action, model output, retrieved email text, a timeout, dismissing the popup or pressing the recording hotkey cannot approve it. Keyboard accessibility for a focused review button remains supported. Cancel/Close rejects pending unapproved changes. Confirm each write separately; never approve the whole session or a batch implicitly. After execution, show the actual tool outcome, including failures or uncertain outcomes, in the same branded popup.

## Technical Approach and Alternatives

The recommended approach is a local Codex app-server bridge, using its existing supported authentication/configuration and app/MCP connections. It supplies discovery, tool execution, conversation state, streamed activity, interruption and approval RPCs. It is a distinct agent-backed request path, not an extra field on SharpVoice's current one-shot CodexPayload.

Directly exporting this chat's tools or connector credentials is not a supported integration contract and is excluded. Implementing Gmail/Outlook OAuth inside SharpVoice duplicates the existing connections the user wants to reuse. A paid hosted agent/API backend conflicts with the user's existing exclusion of Platform API. App-server is therefore the practical approach to verify first, subject to its actual connection availability.

Windows and WSL Codex can have different profiles/connections. Do not copy credentials between them or silently switch accounts. Detection must identify the runtime/profile used and explain unavailable connections. Initial implementation may limit runtime support explicitly rather than pretending parity.

## Bridge and Permission Contract

Initialize an app-owned transport and conversation with client name SharpVoice. Do not attach to or change the user's active Codex conversation. Use app/installed and app/list for effective app availability, app/read public tool summaries where supported, and MCP status discovery for configured servers. Enumerate paginated results with bounds and tolerate version differences through an explicit compatibility check.

Enforce selected integrations and restrictive per-tool approval policy in the app-owned process/thread configuration, preserving existing disabled tools and organizational restrictions. Do not write global config.toml. The implementation must prove through fixtures that existing permissive per-app/per-tool overrides cannot bypass SharpVoice confirmations. If the runtime cannot enforce integration and capability restrictions, connected requests remain unavailable, including reads; prompt instructions alone are insufficient.

Suppress unrelated local shell/file-edit capabilities for connected assistant work. Do not send the active editor's source text, selected text, absolute file paths or repository instructions into email requests by default. Treat fetched email and tool results as untrusted data. Limit retrieved content and retained history; do not log raw email bodies, credentials or full tool arguments containing personal data. Preserve required action details in the visible preview only for its pending lifetime.

Correlate thread, turn, tool item and approval request IDs. Bind the displayed diff and on-screen confirmation to the real destination and exact final action arguments. Confirm exactly one pending action; reject stale/double approvals. Do not approve for the session. Do not repeat side-effecting calls after an interrupted transport or uncertain result. Report “outcome unknown” and offer to inspect the external service. A matching completed tool result, not model prose, determines whether an action succeeded.

Closing/cancelling the response or exiting interrupts the app-owned turn and declines pending approvals. Cancellation cannot undo an action already accepted by the external service; show completed or uncertain outcomes honestly. Readout starts only for the final answer and uses existing microphone/speech coordination.

## Builder Decisions Following Independent Review

Keep conversational context while the connected response pill is open, with typed/dictated follow-up and clarification. Closing it ends the conversation. Resolve “that email” only from this conversation's actual retrieved results. Changing runtime/profile or integration allowlist cancels work, invalidates pending approvals and clears context.

Approval requires the exact tool and final arguments before execution, rendered as the mandatory branded before/after change review above. Display recipients including CC/BCC, subject, content, attachments and destination where applicable. Refuse actions whose runtime cannot expose a complete preview. Multi-action requests require confirmation for each external write. Any change to approved arguments requires new confirmation. Capability restrictions apply to reads as well as writes; no unrestricted degraded mode.

Independent builder review found no further product decisions needing clarification before presenting the proposal. The above are implementation choices to make the requested workflow concrete. Runtime connection reuse remains a feasibility check, not an assumed entitlement.

## Verification and Delivery

First implement a narrow compatibility/discovery vertical slice with a fake app-server and isolated profiles. Prove discovery, restrictive tool exposure and pre-execution action approval before connecting it to live assistant requests. Then integrate the native preferences, branded activity popup, source links, clarification and mandatory diff/confirmation UI. The feature cannot be considered complete with a text-only or voice-only approval flow.

Fixtures cover unavailable/expired connections, pagination, profile identity, version mismatch, read results, ambiguous email references, untrusted mail instructions, denied/unknown tools, inherited permissive overrides, mandatory popup for reads, accurate creation/edit/deletion diffs, full recipient/attachment disclosure, before-state freshness, voice-only approval rejection, preview-before-write, stale/double approval, cancel-before-confirm, transport failure after a write, bounded output, shutdown and speech/microphone coordination. Tests use synthetic emails/tool results; no live mail reads or sends without a specific user test request. Maintain Native AOT support and existing dictation/VS Code regressions.

## Source Evidence

Read-only recon: installed CLI reports 0.162.0-alpha.17.2; its generated protocol schema is under `.build-cache/codex-connected-schema`. No live app-server session, account lookup, email retrieval or action was started.

Official app-server documentation: https://learn.chatgpt.com/docs/app-server (app discovery, invocation, MCP calls and approval requests).
Configuration: https://learn.chatgpt.com/docs/config-file/config-reference (per-app/per-tool enablement and approval overrides).
Open harness integration overview: https://developers.openai.com/blog/codex-as-a-platform .
The separate token-sharing route's hosted-connector limitations must not be mistaken for proof that every existing account connection is accessible: https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations .

## User Review Outcome

The user likes the Connected Ask AI recommendation and requires a branded popup showing what is being done/changed, with a diff-type view for changes. This is a mandatory acceptance criterion, not an optional presentation enhancement. Voice-only action execution/approval is excluded. Preserve the proposed off-by-default setting, integration opt-in, visible automatic read activity and explicit on-screen approval for every external write. Packaging/runtime/profile selection remains an implementation choice, and actual compatibility must be displayed rather than promised.
