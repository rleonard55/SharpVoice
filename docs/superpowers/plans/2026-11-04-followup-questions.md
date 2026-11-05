# Follow-up Questions Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task.

**Goal:** Highlight and announce plugin questions with optional safe quick replies for Ask AI and code edits.
**Architecture:** Extend bounded structured results and conversation metadata; derive the active prompt from authoritative session or latest conversation turn. Keep sound at new live-response completion sites.
**Tech Stack:** C#, TypeScript, VS Code webview, embedded PCM WAV.
**Spec:** docs/superpowers/specs/2026-11-04-followup-questions-design.md

## Global Constraints
- Four choices maximum, each 200 characters maximum, distinct and nonblank.
- Freeform typed/voice replies remain available; literal rendering; no automatic choice.
- Preserve existing working-file changes and native desktop answers.

## Review Focus
- Stale and repeated clicks must not submit to another prompt.
- Refreshes must not erase code questions or repeat sounds.
- Unsupported/malformed model metadata must not modify a file.
- General Ask replies must preserve file-context selection.
- Busy/canceled/restored conversations must show only current questions.

### Task 1: Structured contracts and providers
- [x] Add failing C# and extension tests for bounded choices and structured Ask output.
- [x] Implement optional choices on editor results/conversation turns and strict Ask response parser.
- [x] Opt plugin Ask into structured output and preserve plain native popup behavior.
- [x] Verify contract/provider tests.

### Task 2: Highlighted card and correlated replies
- [x] Add failing rendering/intent/controller tests for literal choices and stale clicks.
- [x] Add pinned accessible card, preserve active session results on refresh, validate exact prompt and option before routing.
- [x] Correlate general choices with authoritative latest server turn.
- [x] Verify extension suite and native VS Code fixture.

### Task 3: Cue and delivery
- [x] Add distinct embedded cue with asset verification and live guarded completion routing.
- [x] Run desktop and extension suites, request independent review, resolve findings.
- [x] Package matching extension and Native AOT app; verify and stage update; document activation.

Verification: 66 extension tests, 204 desktop checks, native VS Code fixture, clean managed/Native AOT builds, package/current-source and embedded-resource identity checks. Independent review fixes recorded in `.build-cache/followup-review.md`. Delivery hashes in `.build-cache/followup-delivery.json`.
