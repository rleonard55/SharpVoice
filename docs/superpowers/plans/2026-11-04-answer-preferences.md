# Response Pill Preferences Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Dismiss answers on outside clicks and give users independent persistent speech and answer-length controls.

**Architecture:** Extend AppConfig and native Smart Ask controls. Feed answer preferences into provider instructions; use the existing answer cancellation path on dismissal. Install a temporary mouse observer for visible response pills.

**Tech Stack:** C# / .NET 10, Win32, existing executable test harness.

**Spec:** docs/superpowers/specs/2026-11-04-answer-preferences-design.md

## Global Constraints

No added dependencies. Preserve non-activating display, routing contracts, conversation memory and original insertion content. Speech preference defaults on for compatibility; response length defaults Brief.

## Review Focus

Outside clicks before activation; child control/menu clicks; dismissal while a reply is pending; speech toggles during inference; migration of absent JSON fields.

### Task 1: Saved preferences and provider instructions

Files: src/SharpVoice.Core/AppConfig.cs, src/SharpVoice.Refiner/UnifiedRefiner.cs, tests/SharpVoice.Tests/SmartAskChecks.cs.

- [x] Add regression checks for migration, equality, invalid length values, saved speech/length and outgoing provider instructions for all lengths, including follow-ups.
- [x] Run checks and observe missing behavior.
- [x] Add AnswerSpeechEnabled and AnswerVerbosity, migrate absent fields, validate and compare them; append scoped answer-length instructions to Smart Ask and answer calls.
- [x] Run checks and verify routing/insertion tests remain green.

### Task 2: Settings, speech toggle and outside-click dismissal

Files: NativeDesktop.SmartAsk.cs, NativeDesktop.Answer.cs, new NativeDesktop.AnswerDismissal.cs, SharpVoiceApp.Answer.cs, SharpVoiceApp.cs, WindowsHelpSmoke.Answer.cs.

- [x] Add native checks for speech toggle/settings persistence, inside/outside clicks and response visibility before focus.
- [x] Run native smoke and observe missing controls.
- [x] Add independent read-aloud and response-length choices, persistent speech toggle callback, automatic speech gating for new answers/follow-ups, temporary forwarding mouse hook with stale-display protection and menu exemption.
- [x] Run full managed suite and isolated native UI smoke; build the app and inspect the diff.

## Execution notes

Proceed inline under the user's approval of the concrete behavior; no additional permission gate is needed for reversible local edits. Do not restart the running application or change the user's settings during validation.

## Verification and review

- Initial preference regression failed because speech/verbosity defaults were missing; native UI regression failed because the speech toggle was missing.
- Independent review identified native EDIT context-menu clicks being treated as outside clicks. An actual WM_CONTEXTMENU regression reproduced dismissal/draft loss. The fix uses GUI thread menu ownership and the final native suite passes. Review found no remaining blockers.
- Final managed build: zero warnings/errors (`.build-cache/answer-preferences-menu-build.log`).
- Final complete executable suite: 196/196 (`.build-cache/answer-preferences-final-tests.log`).
- Final isolated native UI suite: passed, including inside/outside clicks before focus, stale clicks, replies updating the same visible pill, native context menus, saved preferences and both OS themes (`.build-cache/answer-preferences-final-ui.log`). Captured settings and pill layouts were inspected.
- The first full-suite invocation used dotnet.exe as the host and failed five executable/subprocess checks. Running the actual test executable corrected the harness invocation; the complete suite then passed. The original log is retained.
- Actual hardware clicks/audio and live-model response length remain manual acceptance checks documented in docs/windows-smoke-test.md. Automated checks use isolated UI, temporary settings and synthetic provider responses.

- Native AOT app publication and bootstrap passed without warnings/errors. Update staged for Launch-SharpVoice.cmd; staged/portable/isolated executable SHA-256 match: 147f12c97d0daf7865e9e9a89751344b2217d4c866e5693c004d1d981805570e. The first packaging attempts encountered WSL SDK lookup and locked shared intermediates; isolated publication succeeded. No running app was restarted.
