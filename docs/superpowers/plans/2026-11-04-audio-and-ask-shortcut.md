# Audio and IDE Ask Shortcut Implementation Plan

> **For agentic workers:** Use test-driven implementation; independent UI audio work may be delegated using dispatching-parallel-agents.

**Goal:** Automatic Audio-tab monitoring and plugin capture of the configured Ask shortcut.
**Architecture:** UI monitor eligibility drives existing recorder preview; authenticated hotkey gate carries ask/dictate mode and routes Ask to read-only workspace voice capture.
**Tech Stack:** C#, Win32, TypeScript, VS Code APIs.
**Spec:** docs/superpowers/specs/2026-11-04-audio-and-ask-shortcut-design.md

## Global Constraints
- Saved mic only; no transcription or AI from monitoring; reuse recorder ownership.
- Ask never inserts source; file context stays explicit; preserve outside-editor behavior.
- Correlated request ID/client/mode/focus/expiry; hold/toggle microphone readiness required.

## Review Focus
- Nested Ask while Dictation handoff or capture is active.
- Pending Ask canceled/hidden/focus lost during connection cannot start later.
- Recording key release before readiness and one-press toggle stop.
- Audio tab exits, speech playback and settings startup without real device access in tests.
- Paired package/protocol and no running-process disruption.

- [x] Test and implement automatic UI monitoring/indicator/lifecycle and native hidden-desktop fixture.
- [x] Test and implement Ask handoff, workspace capture, promotion and plugin routing.
- [x] Run native editor/app fixtures, full suites and independent review.
- [x] Package, verify embedded VSIX and stage paired app/plugin update; update docs.
