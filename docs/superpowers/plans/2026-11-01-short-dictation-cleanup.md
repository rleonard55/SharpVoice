# Short Dictation Cleanup Threshold Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with final source review and checkbox tracking.

**Goal:** Skip AI cleanup for ordinary dictation below 8 words by default, with configurable threshold and 0 disabling skips.
**Architecture:** Shared Core policy counts whitespace-delimited tokens containing a Unicode letter or digit. UnifiedRefiner bypasses before authentication/provider/model work; app/HUD uses the same policy for accurate feedback. Ask AI and selected-text edits never bypass.
**Tech Stack:** C# .NET10 Native AOT, generated JSON config, existing Win32 Settings.
**Spec:** User-approved eight-word proposal in conversation (ordinary 1–7 words raw, 8+ refined; 0 always refine).

## Global Constraints
- Apply only to ordinary dictation; preserve Ask AI and selected-text behavior.
- Default 8 on new and legacy configs; preserve explicit0.
- Whitespace separates words; contractions/hyphenated tokens count once; punctuation-only tokens do not count.
- Preserve raw transcript exactly; skip all provider/auth/model work and events on bypass.
- Configurable integer0–1000 under AI & models; preserve six-tab navigation and General startup setting.
- No real OAuth, live inference, saved user config changes or running-app interruption are needed.

## Review Focus
- Boundary7/8 words, newlines, Unicode whitespace and punctuation.
- Legacy absence versus explicit disabled 0 in source-generated config.
- Short Ask AI/selected edits and backend-none errors must keep their existing semantics.
- Every backend bypasses before network, CLI preparation or embedded model loading.
- HUD must reset skip state for subsequent dictation and not report an AI model as used.

### Task1: Policy and persistence
Files: Core/AppConfig.cs; new Core/RefinementPolicy.cs; new tests/RefinementThresholdChecks.cs; tests/Program.cs.
- [x] Add a failing regression demonstrating short text currently reaches provider authentication.
- [x] Add default 8/persisted0/legacy migration/range and7/8-word boundary checks.
- [x] Implement shared policy and generated config field.

### Task2: Refiner, Settings and HUD
Files: Refiner/UnifiedRefiner.cs; App/SharpVoiceApp.cs; UI/NativeDesktop.Settings.cs, .Hud.cs, .Models.cs; tests/WindowsSettingsSmoke.cs.
- [x] Implement bypass after normal backend/selection validation, before provider work.
- [x] Reflow AI & models for the minimum-words field and explanation; save/display draft values normally.
- [x] Show skipped cleanup in status/HUD and reset that state on the next recording.
- [x] Keep provider-protocol fixtures explicit about threshold0, and test Ask/selection exemptions with default 8.
- [x] Verify native preference round-trip, edited0/8 and validation failure behavior; inspect light/dark layout.

### Task3: Deliver
- [x] Managed/native regression suites, native Settings default/custom, strict AOT builds.
- [x] Independent read-only review; fix Important issues.
- [x] README/smoke/verification update and verified staged/portable executable with update.ready.

## Ledger
- The user's approval is for this threshold; the seven other feature ideas remain proposals.
- Preserve ChatGPT default, Gemini/Claude support and current tray layout.
- No Git staging/commits: workspace is untracked under a parent Documents repository.

- Initial red:68/69 with short dictation unexpectedly invoking authentication. Managed green:71/71 plus native Settings default/custom. Independent final source review found no Important issues.

- Final managed and Native AOT: **71/71** each. Settings default/custom pass in both builds; light/dark previews inspected and helper spacing corrected. Strict native app/test publication and managed build pass without warnings/errors. Staged/portable executable verified; launcher update marker created after checks.
