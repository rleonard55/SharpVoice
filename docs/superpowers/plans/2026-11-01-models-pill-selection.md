# Model visibility, pill HUD and selected-text voice editing

> **For agentic workers:** Use superpowers:executing-plans for assigned implementation tasks. Do not commit or stage.

**Goal:** Make model choice understandable and economical, modernize the HUD, and use speech as editing instructions when text is selected at hotkey press.

**Architecture:** Keep native Win32/Direct2D and Native AOT. Use the authenticated account catalog for available ChatGPT models, present concrete resolved model names, and choose a known economical small model under auto when available. Never imply exact account pricing/latency where the catalog lacks data. Preserve explicit user model choices. The HUD becomes a compact rounded pill with concise state/model labels and a small waveform; expanded preview can remain concise. Capture selection without modifying clipboard or sending copy keystrokes. Use a separate editing prompt so the dictation master prompt's instruction prohibition does not conflict with explicit voice editing. Verify the source selection remains current before replacing; retain results on focus/selection mismatch.

**Approaches:** Build on the native shell and catalog rather than replacing the UI framework or adding a paid Platform backend. Use accessible selection APIs and direct native edit-control support instead of clipboard shortcuts while Ctrl+Alt is held. These choices preserve current deployment and input behavior. Existing defaults remain Ctrl+Alt, tiny.en and ChatGPT OAuth; auto model selection changes to the economical policy.

## Tasks

- [x] Refiner: discover model choices with descriptions/recommendation hints, economical auto ranking, exact resolved-model reporting and tests. Add optional selected-source input with a separate editing prompt and tests for empty speech/local-only behavior.
- [x] Platform: capture selected text on hotkey press with bounded native selection reads; retain a selection snapshot and refuse replacement if focus/selection/text changes. Tests use isolated native edit controls, no clipboard mutation for discovery.
- [x] UI/coordinator: account model picker, resolved model label and refresh behavior; compact pill HUD; capture selection before recording, send spoken editing instructions and replace selection only after validation.
- [x] Verify managed behavior, native selection/HUD/settings tests, strict AOT app/test publishes; update docs and stage launcher update without closing the running app.

Success ding added per follow-up: subdued 140 ms bell after successful insertion, verified PCM properties and native playback. Cold-output silent startup priming verified. Interactive cross-app/provider QA remains in docs/windows-smoke-test.md.
