# On-Demand Settings Help Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline with final independent review.

**Goal:** Replace persistent explanatory paragraphs with contextual hover/click/keyboard help across all six tabs.
**Architecture:** An owned, nonactivating native popup provides temporary hover help and pinned click/keyboard help. Native STATIC text and descriptive buttons preserve accessibility and AOT support. Settings status drops routine shortcut reminders; model status keeps actual IDs and a compact Gemini retention notice.
**Tech Stack:** Existing Win32/C# UI and comctl32, .NET10 Native AOT. No dependencies.
**Spec:** User requests context on hover/click rather than continuously visible text; screenshot marks routine status, cutoff explanation and long model notes.

## Global Constraints
- Preserve all settings, choice/ID mappings, validation, provider routing, mic activation/preview and prompt drafts.
- Help buttons have descriptive accessible names and keyboard activation; Escape, outside click, focus/navigation/close dismiss help.
- Help must not activate a microphone, authenticate, save settings or steal focus.
- Keep actual error/status messages and actual model IDs; move full details and guidance to help.
- Keep essential Gemini retention notice visible; full context also remains in help.
- Help text stays managed; native STATIC controls own displayed text. Timer/window/region lifetime is bounded by the UI; theme colors follow app theme.
- Preserve running app/user configuration and stage only after verification.

## Review Focus
- Help Enter/Space cannot trigger Apply or double-toggle; Tab navigation remains usable.
- Stale/hidden button commands cannot show another tab's help; dismiss on close/deactivation.
- Dynamic provider/model and status help stays current without stale or freed text pointers.
- Popup geometry/lifetime uses Windows x64 ABI, no focus activation or hidden UI side effects.
- Unchanged Apply and edited preferences/prompt drafts continue round-tripping.

### Task 1: Help behavior
- [x] Add isolated-desktop native help regression; observe missing-control failure.
- [x] Implement owned native popup, descriptive help buttons, hover/pinning and keyboard/dismiss behavior.

### Task 2: Declutter
- [x] Move explanations into per-setting help; reflow six tabs and retain model ID/critical notices.
- [x] Compact routine Settings status while preserving full errors and original HUD/tray status.
- [x] Native Help and Settings smoke, managed suite, light/dark/tab/popup preview inspection.

### Task 3: Deliver
- [x] Final independent source review; fix material issues with regression evidence.
- [x] Strict native app/test build, native suite and Help/Settings checks.
- [x] Docs/verification, verified staged executable and update marker; leave locked running copy alone.

## Ledger
- User specified the interaction pattern. Existing native controls are appropriate; no new external service or approval round.
- No Git staging/commits: untracked workspace under parent repository.
- API layout/behavior verified against Microsoft TOOLINFO and TTM_TRACKACTIVATE documentation; links recorded with implementation notes.

- Implementation adjustment: tracking-tooltip registration/text succeeded but visibility failed in repeated native checks, including a guarded input-desktop fixture. Replaced it with an owned nonactivating native window, with actual native hover/click/rendering checks passing on an isolated desktop. No retained unmanaged text pointers or tooltip message constants remain.

- Final independent review found one Important issue: queued hover timers could reopen help after Settings deactivation. The native regression reproduced it before the fix (`settings-help-demand-review-red.log`). Dismissal now invalidates pending hover state and each timer gets a unique ID; regressions cover stale queued messages and re-arming after dismissal. Moving/minimizing Settings also dismisses help. No other Important/Critical findings.

- Final validation: managed and AOT 76/76 checks, native Help and Settings default/custom checks pass. All six pages and popup inspected in light/dark. Staged executable 13,889,024 bytes, SHA-256 `6f198227c128b4801269134f6303851352aa0a926cb917ac92639008080c2e4d`. Manual DPI/multi-monitor/screen-reader acceptance remains on the checklist.

- Delivery: the portable executable was no longer running at final read-only process check, so it and its symbols were updated. Portable/staged executable SHA-256 values match. No instance was stopped or launched.
