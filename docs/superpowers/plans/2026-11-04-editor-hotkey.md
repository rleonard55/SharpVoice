# Editor Hotkey Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline.

**Goal:** Route the configured Dictation shortcut in foreground VS Code to fresh-context code previews requiring Apply.
**Architecture:** Desktop requests a correlated start over the authenticated bridge; the focused plugin captures its current file then reuses start/validate/result/apply. A small state gate rejects stale handoffs. Ask AI remains on the general path.
**Tech Stack:** .NET 10 / Win32 / TypeScript VS Code extension.
**Spec:** docs/superpowers/specs/2026-11-04-editor-hotkey-design.md

## Global Constraints

Protocol 4 / extension 0.2.1. Preserve custom shortcuts and hold/toggle semantics. No automatic application or paste fallback; no live user-session testing.

## Review Focus

Modifier promotion to general Ask, release before plugin/microphone readiness, other editor windows, app restart/reconnect, invalid editor focus and late frames.

### Task 1: Correlated desktop handoff

- [x] Add gate tests for correlation, release/cancel, expiry and accepted ownership; observe failure.
- [x] Implement EditorHotkeyGate in Core, focused window/client binding in SharpVoiceApp.EditorHotkey.cs, and Dictation-only shortcut interception. Honor release, cancel, disconnect and Ask promotion.
- [x] Preserve editor source checks and use existing host environment boundaries for isolated integration verification.

### Task 2: Focused plugin listener and fresh context

- [x] Add real VS Code host tests for desktop-triggered start, fresh C# selection/language, diff-before-Apply, cancellation during startup and inactive-window rejection; observe failure.
- [x] Add background focus-aware bridge subscription/reconnection; implement correlated hotkey_start/hotkey_cancel. Reuse existing code capture, validation, preview, Apply and Undo.
- [x] Update protocol/version and bundle the exact verified VSIX; update user guidance.

### Task 3: Verification and delivery

- [x] Run managed suite, Node suites and real isolated VS Code host integration, with no live microphone or provider use.
- [x] Request independent code review, repair findings and rerun affected checks.
- [x] Publish native app, verify bootstrap/package hashes and stage the launcher update.

## Execution notes

The user confirmed Only Dictation; no additional design permission is needed for these reversible local changes. Native keybindings are intentionally not hardcoded. If plugin context is unavailable, fail with a connection/editor message rather than paste into source.

Ruling: The user requested window-level routing. VS Code keeps its current active editor while its terminal has focus and exposes no supported context-key query. Preserve window-level routing by visibly restoring the current active source and reading it fresh; refuse when no active source exists. Remove the initially invented blanket terminal-refusal requirement. No paste or automatic source mutation occurs.
