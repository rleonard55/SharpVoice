# Settings refinement: compact cards in the existing tabs

Status: approach selected; design and interactive preview awaiting owner approval. Product implementation has not started.

## Intent

Extend the approved Indigo compact input treatment to microphone selection, readiness and wake phrases, and review the other native forms for clearer grouping, less wasted space and more understandable state. Keep settings easy to discover and preserve existing voice, provider, update and insertion behavior.

The owner selected **compact cards within the existing tabs** over broader settings-navigation redesign. Preview: `artifacts/design-mockups/settings-refinement/index.html`. This standalone HTML prototype uses simulated data, performs no device/provider requests and does not change application settings. Dialogs other than Wake are illustrative sketches, not complete implementation specifications.

## Approaches

1. **Selected: compact cards, existing tabs.** Group related controls, use concise state summaries and size each dialog to its task. Incremental native Win32 changes; familiar navigation remains. Some current information architecture remains imperfect.
2. **Considered: redesign settings navigation.** A sidebar could consolidate Audio/Hotkeys and split General into dedicated pages, but moves familiar settings and expands migration, navigation and testing scope. The owner chose the smaller approach.

## Scope and sequence

This is a review and design proposal, not authorization to implement all forms at once. Recommended implementation order:

1. Audio and Wake Phrases, plus shared card spacing, labeled switches and status treatment.
2. Existing AI & models, Prompts, Hotkeys, Insertion and General tabs.
3. Supporting tools/dialogs in smaller reviewed batches.

Approve phase 1 first. Later phases require their own exact form designs and interaction details before product code changes. No new release is authorized by this design request.

## Phase 1: Audio

- Keep the Indigo brand header and all six tabs. Group microphone selection into a card containing device picker, compact Refresh action with an accessible name, device hint and monitoring state. When selection differs from the applied device, show the applied device name alongside the pending-selection message, wrapping long names.
- Refresh remains a real explicit action. Do not introduce a new preview-microphone button. Reuse current automatic monitoring lifecycle, including suspension while hidden, minimized, recording, paused or playing responses and failure latching.
- Clearly distinguish draft device selection from the applied microphone being monitored. Selecting another device does not begin monitoring it until Apply. Unavailable, suspended, paused and failed states need text and help, not color alone.
- A second card groups microphone readiness (On hotkey / Keep ready) and a Wake Phrases row with actual enabled/preparation status and Configure action. Preserve current activation IDs, defaults and recording gesture settings. Readiness is independent of Hold/Toggle.
- Readiness choices may look segmented but must use an accessible native radio group or equivalent keyboard-selectable group. Keep hotkey start/stop configuration in Hotkeys.
- Wake status reflects the real preparation/listening state, including off, preparing, ready, paused, suspended and unavailable. Derive paused/readout/editor suspension from runtime conditions and messages; the runtime `ready` state alone does not mean active listening. Avoid claiming active listening merely because enabled is saved. Explain that enabled wake phrases can keep listening regardless of readiness mode.
- Reuse the approved combined meter and exact gate field, keyboard/drag behavior, Live Transcript switch and Apply semantics. Keep Last Result accessible in Audio.

## Phase 1: Wake editor

Use a focused compact owned dialog rather than the shared 856×780 tool canvas. It contains a labeled enable switch, dictation phrase, Ask AI phrase, silence stop (existing 1–10 seconds), preparation status and one Save Changes action plus Cancel. Disabled phrase fields retain their values when wake is Off. Save validates existing rules (distinct normalized phrases, 2–6 English words each, at most 80 characters, and an integer 1–10 second silence stop, even when Off) and saves wake settings only; Cancel/Escape discards its draft.

Keep preparation progress, cancellation and retry visible whenever actionable; general background explanation can be collapsed. The enable switch changes only the preference draft; current operation is displayed separately until saved. Retry/cancel preparation act immediately on current work and are not undone by dialog Cancel. Saving enabled wake settings may start preparation/listening according to current behavior; disclose that near the enable switch. Keep the existing microphone pause and active-operation restrictions. Preserve automatic preparation cancellation/retry behavior.

Wake dialog has its own save scope. Audio draft edits stay pending while it is open and after it saves. Applying the parent must preserve wake changes made by the child, and saving the child must preserve the parent's unrelated draft fields. No implicit parent Apply when opening a dialog. Verify this explicitly with mixed drafts.

## Review findings and later proposals

| Form | Current source finding | Proposed refinement |
|---|---|---|
| AI & models | Speech recognition and provider/model/account/test actions share one flat form, with conditional actions sharing fixed positions | Separate local speech and optional AI cards. Co-locate selected provider, model, account, test result and recovery. Keep all supported providers and editable model IDs; show only relevant actions. |
| Prompts | General/mode editors are separate, but the shared Last Result consumes space | Explain instruction scope; place mode selector beside editor; preserve per-mode drafts. Reserve content area for editing. |
| Hotkeys | Shortcut selectors and record buttons occupy separate rows; labels always say Hold | Put task, keys and Change action on one row. Follow selected Hold/Toggle gesture in labels. Disable irrelevant silence-stop input without clearing its value. |
| Insertion | Three settings occupy a large generic form | Use compact groups with nearby units; preserve method, delay and recording-limit ranges. Keep their tab locations. |
| General | Flat grid of eight tool buttons plus startup/history dropdowns | Labeled startup switch, clear history choice, grouped writing tools and integrations with short descriptions. |
| Plugins | Location, included version, installed version and status use full read-only edit rows | Compact editor status cards and local next action; expandable selectable/copyable path details. Preserve install/refresh/cancellation. |
| Updates | Large shared tool window; update policy, verbose details and multiple manual actions spread vertically | Group version/status/action and policy. Retain Check, Download and one-click Install and Restart; manual actions remain independent of automatic policy. |
| Dictionary / snippets / profiles | Useful list/editor structure, but entry draft and whole-form save require explanation | Keep list/editor, show New versus Editing, explain Add/Update versus Save. Clear inherited/global settings in profiles; retain every override. |
| Smart Ask | Four dropdowns on a generic tool canvas | Group response behavior separately from speech/voice; switches for booleans. Preserve Microsoft Edge voice disclosure and Windows voice fallback. |
| Connected Ask | Tool list, runtime, permissions and account actions are dense | Clear connection summary and allowed-tools list, expandable runtime details; preserve all explicit tool permissions and confirmation requirements. |
| History | List, two large text editors, retry controls and destructive actions | Keep text readable, group retry model/provider/action; separate deletion from Copy. Retain text-only storage explanation and never auto-paste retries. |
| Diagnostics | Two large read-only blocks with long disclosures | Clear failure summary plus exact selectable report; keep copy/upload/privacy consequences visible. No automatic uploads. |
| API key | Short task on a large generic tool canvas | Focused credential dialog; preserve masked entry, storage explanation, blank behavior, remove action and provider setup instructions. |
| Setup | Four-step flow already has a clear sequence | Reuse visual tokens and short explanations. Keep explicit Test Microphone; do not import Settings' automatic monitoring into onboarding. |

## Common conventions

- Native C#/Win32/GDI implementation remains the builder choice, using existing theme palette. No WebView, new framework, dependency or provider changes.
- Common card spacing and hierarchy; labels, units, contextual help and one primary save action per form. Style conveys action/state without relying on color alone.
- Native keyboard focus and accessible names/check states; icon-only controls get accessible names and help. Native tab/radio/checkbox semantics remain intact.
- Natural dialog sizes must remain usable at supported DPI/scaling and with long microphone/model names. Do not simply shrink fixed pixels until text clips.
- Later proposal: show Last Result on Audio only and reclaim its area on other tabs, while retaining an obvious copy/result path. This needs approval as part of phase 2.
- No auto-save migration. Preserve distinct parent Apply and child Save scopes, validation, cancellation, busy states, provider disclosures and recovery actions. Unsaved state must stay visible across tabs.

## Verification required for implementation

Phase 1: native default/custom round trips, applied versus draft microphone state, refresh/disconnection and monitoring lifecycle; readiness keyboard/mouse and stored IDs; wake off/on/preparing/failure/paused states, numeric/phrase validation, Save/Cancel/Escape, parent/child draft isolation; combined meter regressions; dark/light/high-contrast and supported scaling/long-label renders; full offline suite and Native AOT build. Subsequent forms need focused regressions for their changed behavior, not just coordinate snapshots.

## Builder check

A fresh read-only review identified missing operational-state examples, phrase validation, radio keyboard semantics and applied-device identity in the preview. These were resolved within the selected approach; no additional owner-only decision was required. The preview includes explicitly labeled simulated preparation scenarios; those scenario controls are not proposed native product controls.

## Preview verification

Real Edge browser checks pass for six tabs and arrow-key navigation; microphone draft versus Apply; fractional/invalid gate; readiness radio arrows; wake Save/Cancel draft retention, distinct normalized phrase validation (including SharpVoice versus Sharp Voice), numeric bounds, current download/progress/cancel/retry/ready-suspension states and synchronized parent badge; gesture-dependent auto-stop controls; mode prompt draft retention; provider-specific actions; supporting dialog launch/close and theme switching. Dark/light and wake-download captures were visually inspected. The preview is an illustration, not a native scaling/accessibility certification; no product code or installed setting was changed. Evidence: `.build-cache/settings-refinement-check-dom.html`, `artifacts/design-mockups/settings-refinement/preview-*.png`.
