# Compact Audio Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Implement the approved compact meter/gate/transcription card in native Audio Settings.
**Architecture:** Reuse a native trackbar for threshold input and its accessibility/keyboard behavior; custom-draw the live signal and gate handle. Keep native numeric entry and a native checkbox with custom switch presentation. Preserve current configuration keys and Apply semantics.
**Tech Stack:** C# / Win32 / GDI, Native AOT.
**Spec:** docs/superpowers/specs/2026-11-05-compact-audio-control-design.md

## Global Constraints

- Existing −90 to 0 dB gate range and Apply behavior; no provider, audio-capture or monitoring changes.
- Preserve native control semantics, theme contrast, help and keyboard focus.
- Retain existing unpublished ChatGPT Ask compatibility changes; do not publish or commit during implementation.

## Review Focus

- Typing invalid/fractional dB values must remain visible and never silently become a saved rounded value.
- Slider keyboard/drag semantics and rendered threshold must agree at both endpoints.
- Native switch state must synchronize with Apply/configuration and its accessible checked state.
- Audio-only Last Result repositioning must restore when changing tabs.
- Paused/waiting readings and theme changes must repaint without brush or input-handler leaks.

## Task 1: Combined meter and threshold

- [x] Add a focused native compact-input regression and observe its initial layout failure.
- [x] Update `NativeDesktop.Meter.cs`, `NativeDesktop.Settings.cs`, `NativeDesktop.Presentation.cs` to custom-draw the native threshold trackbar as the combined live meter.
- [x] Preserve numeric synchronization and help IDs, range, accessibility and keyboard/drag behavior.
- [x] Verify native threshold interaction, invalid values, level/gate comparisons, dark/light render captures.

## Task 2: Compact card and Live Transcript switch

- [x] Replace the Live Transcription dropdown with an accessible native checkbox/switch in the card's bottom row.
- [x] Update native config reads and toggle visuals, card outline, Audio-only result placement and help.
- [x] Verify toggle persistence, Apply behavior and tab restoration in the native fixture.

## Task 3: Verification and local delivery

- [x] Run full desktop regressions and focused native settings/monitoring checks; update coordinate-specific meter snapshot assertions for the combined control.
- [x] Inspect actual dark/light captures; build Native AOT and verify noninteractive startup.
- [x] Obtain a fresh review of this UI change, resolve important findings and document verification.
- [x] Stage verified native files for the launcher with the unchanged plugin; preserve running app and unpublished Ask fix.

## Execution notes

- Ruling: use the existing workspace because it contains the authorized unpublished Ask fix and local delivery; no branch integration or publishing is requested.

- Completed: native interaction regression first reproduced the old separate layout. Review-driven fractional-value and marker hit-testing regressions also failed before their fixes. Final managed build and Native AOT publish pass; 209/209 desktop checks and focused native interaction/settings/monitoring/render checks pass. Dark/light captures inspected. Local staging manifest: `.build-cache/compact-audio-delivery.json`.

- Subsequent owner instruction: “Yes that is good, publish it.” This authorizes committing and publishing release 0.1.10, superseding the implementation-only publishing constraint above.
